using System;
using System.Linq;
using System.Reflection;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace de4dot.code.deobfuscators.Babel_NET.LegacyVM {
	static class DelegatesCleaner {
		public static int CleanDelegates(ModuleDefMD module, Assembly asm) {
			int replaced = 0;
			if (module == null || asm == null)
				return replaced;

			foreach (var typeDef in module.GetTypes()) {
				foreach (var methodDef in typeDef.Methods) {
					if (methodDef == null || !methodDef.HasBody)
						continue;
					for (int i = 0; i < methodDef.Body.Instructions.Count; i++) {
						try {
							if ((methodDef.Body.Instructions[i].OpCode != OpCodes.Call && methodDef.Body.Instructions[i].OpCode != OpCodes.Callvirt) || !(methodDef.Body.Instructions[i].Operand is MethodDef))
								continue;

							var proxyMethod = methodDef.Body.Instructions[i].Operand as MethodDef;
							if (proxyMethod == null || proxyMethod.DeclaringType == null || !proxyMethod.DeclaringType.IsDelegate)
								continue;
							if (proxyMethod.FullName.Contains("::Invoke"))
								continue;

							TryPrimeDelegateRuntime(proxyMethod.DeclaringType, asm);
							if (PatchDelegateCall(module, asm, methodDef, i, proxyMethod.DeclaringType))
								replaced++;
						}
						catch {
						}
					}
				}
			}
			return replaced;
		}

		static void TryPrimeDelegateRuntime(TypeDef delegateTypeDef, Assembly asm) {
			if (delegateTypeDef == null || asm == null)
				return;
			try {
				var ctors = delegateTypeDef.FindConstructors().ToArray();
				if (ctors.Length > 1 && ctors[1] != null && ctors[1].Body != null) {
					var ctor = ctors[1];
					var cInstrs = ctor.Body.Instructions;
					if ((cInstrs.Count == 5 || cInstrs.Count == 6) && cInstrs[cInstrs.Count - 2].OpCode == OpCodes.Call) {
						var initMethodDef = cInstrs[cInstrs.Count - 2].Operand as MethodDef;
						if (initMethodDef != null) {
							var initMethod = asm.ManifestModule.ResolveMethod(initMethodDef.MDToken.ToInt32());
							if (initMethod != null) {
								int argc = cInstrs.Count - 2;
								var args = new object[argc];
								for (int i = 0; i < argc; i++) {
									if (!cInstrs[i].IsLdcI4()) {
										args = null;
										break;
									}
									args[i] = cInstrs[i].GetLdcI4Value();
								}
								if (args != null)
									initMethod.Invoke(null, args);
							}
						}
					}
				}

				var cctor = delegateTypeDef.FindStaticConstructor();
				if (cctor != null && cctor.Body != null) {
					var cctorInstrs = cctor.Body.Instructions;
					for (int i = 0; i < cctorInstrs.Count; i++) {
						if (cctorInstrs[i].OpCode != OpCodes.Call)
							continue;
						var initMethodDef = cctorInstrs[i].Operand as MethodDef;
						if (initMethodDef == null)
							continue;

						int start = i - 1;
						while (start >= 0 && cctorInstrs[start].IsLdcI4())
							start--;
						start++;
						int count = i - start;
						if (count != 3 && count != 4)
							continue;

						var initMethod = asm.ManifestModule.ResolveMethod(initMethodDef.MDToken.ToInt32());
						if (initMethod == null)
							continue;
						var args = new object[count];
						for (int k = 0; k < count; k++)
							args[k] = cctorInstrs[start + k].GetLdcI4Value();
						initMethod.Invoke(null, args);
					}
				}
			}
			catch {
			}
		}

		static bool PatchDelegateCall(ModuleDefMD module, Assembly asm, MethodDef ownerMethod, int instrIndex, TypeDef delegateTypeDef) {
			Delegate del = null;
			foreach (var fieldDef in delegateTypeDef.Fields) {
				if (fieldDef == null || !fieldDef.IsStatic)
					continue;
				try {
					var runtimeField = asm.ManifestModule.ResolveField(fieldDef.MDToken.ToInt32());
					if (runtimeField == null)
						continue;
					del = runtimeField.GetValue(null) as Delegate;
					if (del != null && del.Method != null)
						break;
				}
				catch {
				}
			}
			if (del == null || del.Method == null)
				return false;

			try {
				if (del.Method.ReturnTypeCustomAttributes.ToString().Contains("DynamicMethod")) {
					var dmr = new DynamicMethodBodyReader(module, del);
					dmr.Read();
					int count = dmr.Instructions.Count;
					if (count < 2)
						return false;
					ownerMethod.Body.Instructions[instrIndex].OpCode = dmr.Instructions[count - 2].OpCode;
					ownerMethod.Body.Instructions[instrIndex].Operand = dmr.Instructions[count - 2].Operand;
					return true;
				}

				var imported = module.Import(del.Method);
				ownerMethod.Body.Instructions[instrIndex].OpCode = OpCodes.Call;
				ownerMethod.Body.Instructions[instrIndex].Operand = imported;
				return true;
			}
			catch {
				return false;
			}
		}
	}
}
