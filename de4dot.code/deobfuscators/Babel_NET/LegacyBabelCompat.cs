/*
    Copyright (C) 2011-2015 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using de4dot.blocks;
using de4dot.blocks.cflow;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace de4dot.code.deobfuscators.Babel_NET {
	class LegacyBabelCompat {
		readonly ModuleDefMD module;
		readonly string modulePath;
		Assembly runtimeAssembly;
		Version runtimeVersion;
		Version babelVersion;
		readonly Dictionary<string, string> dependencyIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		readonly Dictionary<string, bool> missingDependencies = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
		bool resolverHooked;
		int lastDelegateCandidates;
		int lastDelegateResolved;
		int lastDelegateReplaced;

		public bool Detected { get; private set; }
		public string VersionString { get; private set; }
		public bool VersionIsHeuristic { get; private set; }
		public int DetectionScore { get; private set; }

		public LegacyBabelCompat(ModuleDefMD module) {
			this.module = module;
			modulePath = module == null ? null : module.Location;
		}

		public void Find() {
			int score = 0;
			string version = null;
			VersionIsHeuristic = false;

			if (module == null)
				return;

			if (HasBabelMetadataStream()) {
				score += 4;
			}
			if (HasBabelNamedType()) {
				score += 3;
			}
			if (HasObfuscationAttributeSignals()) {
				score += 2;
			}
			if (HasVmEncryptedMethodPattern()) {
				score += 3;
			}
			if (HasFailFastStubPattern()) {
				score += 1;
			}
			if (HasModuleBangMethodPattern()) {
				score += 3;
			}
			if (HasPrivateUseNameDensity()) {
				score += 2;
			}

			var attrVersion = GetVersionFromBabelAttribute();
			if (!string.IsNullOrEmpty(attrVersion)) {
				score += 2;
				version = attrVersion;
			}
			else if (module.Assembly != null && module.Assembly.Version != null) {
				version = module.Assembly.Version.ToString();
				VersionIsHeuristic = true;
			}

			Detected = score >= 4;
			DetectionScore = score;
			VersionString = version;
			babelVersion = ParseVersion(version);
		}

		public bool InitializeRuntimeAssembly() {
			if (runtimeAssembly != null)
				return true;
			if (string.IsNullOrEmpty(modulePath))
				return false;
			try {
				EnsureAssemblyResolver();
				runtimeAssembly = Assembly.LoadFrom(modulePath);
				runtimeVersion = runtimeAssembly.GetName().Version;
				return true;
			}
			catch {
				runtimeAssembly = null;
				return false;
			}
		}

		void EnsureAssemblyResolver() {
			if (resolverHooked)
				return;
			resolverHooked = true;
			BuildDependencyIndex();
			AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
		}

		void BuildDependencyIndex() {
			dependencyIndex.Clear();
			missingDependencies.Clear();
			try {
				var baseDir = System.IO.Path.GetDirectoryName(modulePath);
				if (!string.IsNullOrEmpty(baseDir))
					IndexDirectory(baseDir, recursive: false);
			}
			catch {
			}
			LogMissingDependencyHints();
		}

		void IndexDirectory(string dir, bool recursive) {
			if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir))
				return;
			var files = System.IO.Directory.GetFiles(dir, "*.dll", recursive ? System.IO.SearchOption.AllDirectories : System.IO.SearchOption.TopDirectoryOnly);
			foreach (var f in files) {
				var name = System.IO.Path.GetFileNameWithoutExtension(f);
				if (!dependencyIndex.ContainsKey(name))
					dependencyIndex[name] = f;
			}
			files = System.IO.Directory.GetFiles(dir, "*.exe", recursive ? System.IO.SearchOption.AllDirectories : System.IO.SearchOption.TopDirectoryOnly);
			foreach (var f in files) {
				var name = System.IO.Path.GetFileNameWithoutExtension(f);
				if (!dependencyIndex.ContainsKey(name))
					dependencyIndex[name] = f;
			}
		}

		Assembly OnAssemblyResolve(object sender, ResolveEventArgs args) {
			try {
				var requested = new AssemblyName(args.Name).Name;
				if (string.IsNullOrEmpty(requested))
					return null;
				string path;
				if (!dependencyIndex.TryGetValue(requested, out path)) {
					missingDependencies[requested] = true;
					return null;
				}
				if (!System.IO.File.Exists(path))
					return null;
				return Assembly.LoadFrom(path);
			}
			catch {
				return null;
			}
		}

		void LogMissingDependencyHints() {
			var unresolved = new List<string>();
			if (module != null && module.Assembly != null) {
				foreach (var asmRef in module.GetAssemblyRefs()) {
					if (asmRef == null || asmRef.Name is null)
						continue;
					var n = asmRef.Name.ToString();
					if (string.IsNullOrEmpty(n))
						continue;
					if (n.StartsWith("System", StringComparison.OrdinalIgnoreCase) || n.Equals("mscorlib", StringComparison.OrdinalIgnoreCase))
						continue;
					if (!dependencyIndex.ContainsKey(n))
						unresolved.Add(n);
				}
			}
			if (unresolved.Count == 0)
				return;

			unresolved.Sort(StringComparer.OrdinalIgnoreCase);
			if (unresolved.Count > 8)
				unresolved = unresolved.GetRange(0, 8);
			Logger.v("[!] Babel runtime dependency hint: missing referenced assemblies may reduce delegate/VM cleanup");
			Logger.v("[!] Missing candidates: {0}", string.Join(", ", unresolved.ToArray()));
			Logger.v("[!] Place missing DLLs next to target assembly and rerun");
		}

		void LogDelegateCleanupHintIfNeeded() {
			if (lastDelegateCandidates <= 0)
				return;
			if (lastDelegateReplaced >= lastDelegateCandidates)
				return;

			Logger.v("[!] Babel delegate cleanup incomplete: {0} unresolved wrappers remain", lastDelegateCandidates - lastDelegateReplaced);
			if (missingDependencies.Count > 0) {
				var names = new List<string>(missingDependencies.Keys);
				names.Sort(StringComparer.OrdinalIgnoreCase);
				if (names.Count > 8)
					names = names.GetRange(0, 8);
				Logger.v("[!] Missing runtime dependencies observed: {0}", string.Join(", ", names.ToArray()));
				var dllNames = new List<string>();
				foreach (var n in names)
					dllNames.Add(n + ".dll");
				Logger.v("[!] Add missing DLLs next to target assembly and rerun: {0}", string.Join(", ", dllNames.ToArray()));
			}
		}

		public bool IsLegacyRuntime {
			get {
				if (babelVersion != null)
					return babelVersion.Major < 8;
				return runtimeVersion != null && runtimeVersion.Major < 8;
			}
		}

		public void RunCleanupPasses() {
			if (!InitializeRuntimeAssembly())
				return;

			Logger.v("[+] Babel compatibility cleanup enabled");
			if (!string.IsNullOrEmpty(VersionString) && !VersionIsHeuristic)
				Logger.v("[+] Babel version: {0}", VersionString);
			else if (!string.IsNullOrEmpty(VersionString))
				Logger.v("[*] Babel version (assembly heuristic): {0}", VersionString);

			Logger.v("[+] Running Babel control-flow prep pass");
			RunControlFlowRound();

			try {
				Logger.v("[+] Restoring Babel VM-encrypted methods");
				LegacyVM.VMDecryptor.allEncMethods.Clear();
				LegacyVM.VMDecryptor.run(module, runtimeAssembly);
				Logger.v("[*] Babel VM candidates: {0}", LegacyVM.VMDecryptor.allEncMethods.Count);
			}
			catch {
				Logger.v("[*] Babel VM restore skipped due to runtime/format mismatch");
			}

			try {
				int totalExact = 0;
				for (int pass = 1; pass <= 5; pass++) {
					int exactReplaced = LegacyVM.DelegatesCleaner.CleanDelegates(module, runtimeAssembly);
					totalExact += exactReplaced;
					Logger.v("[*] Legacy delegate cleanup (ported) pass {0}: replaced={1}", pass, exactReplaced);
					if (exactReplaced == 0)
						break;
				}
				Logger.v("[*] Legacy delegate cleanup (ported) total replaced={0}", totalExact);
			}
			catch {
				Logger.v("[*] Legacy delegate cleanup (ported) skipped");
			}
			CleanDelegateProxyCalls();

			Logger.v("[+] Removing Babel numeric/string encryption patterns");
			RunNumericStringRound(1);
			RunControlFlowRound();
			RunNumericStringRound(2);
			RunControlFlowRound();

			Logger.v("[+] Removing Babel anti-tamper stubs");
			CleanFailFastStubs();

			Logger.v("[+] Running Babel control-flow post pass");
			RunControlFlowRound();

			// Another delegate pass after cflow/constant cleanup catches wrappers that
			// become recognizable only after body simplification.
			CleanDelegateProxyCalls();
			LogDelegateCleanupHintIfNeeded();
		}

		void RunControlFlowRound() {
			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods) {
					if (method == null || !method.HasBody)
						continue;
					try {
						var blocks = new Blocks(method);
						var cflow = new BlocksCflowDeobfuscator();
						cflow.Initialize(blocks);
						cflow.Deobfuscate();
						blocks.RepartitionBlocks();
						IList<Instruction> allInstructions;
						IList<ExceptionHandler> allExceptionHandlers;
						blocks.GetCode(out allInstructions, out allExceptionHandlers);
						DotNetUtils.RestoreBody(method, allInstructions, allExceptionHandlers);
					}
					catch {
					}
				}
			}
		}

		void RunNumericStringRound(int round) {
			Logger.v("[*] Babel cleanup round {0}", round);
			CleanIntegers();
			CleanFloats();
			CleanDoubles();
			if (!IsLegacyRuntime)
				CleanStringsMethodOne();
			else
				Logger.v("[*] Skipping legacy-unsafe string method1 pass");
			CleanStringsMethodTwo();
		}

		bool HasBabelMetadataStream() {
			foreach (var stream in module.MetaData.AllStreams) {
				if (stream.Name == "Babel")
					return true;
			}
			return false;
		}

		bool HasBabelNamedType() {
			foreach (var type in module.GetTypes()) {
				if (type.FullName.IndexOf("Babel", StringComparison.OrdinalIgnoreCase) >= 0)
					return true;
			}
			return false;
		}

		bool HasObfuscationAttributeSignals() {
			foreach (var type in module.GetTypes()) {
				if (type.Name == "BabelAttribute" || type.Name == "BabelObfuscatorAttribute")
					return true;
			}
			foreach (var attr in module.CustomAttributes) {
				var fullName = attr.AttributeType.FullName;
				if (fullName.IndexOf("Babel", StringComparison.OrdinalIgnoreCase) >= 0)
					return true;
			}
			if (module.Assembly != null) {
				foreach (var attr in module.Assembly.CustomAttributes) {
					var fullName = attr.AttributeType.FullName;
					if (fullName.IndexOf("Babel", StringComparison.OrdinalIgnoreCase) >= 0)
						return true;
				}
			}
			return false;
		}

		string GetVersionFromBabelAttribute() {
			// 1) Dedicated Babel attribute type constants
			foreach (var type in module.GetTypes()) {
				if (type.Name != "BabelAttribute" && type.Name != "BabelObfuscatorAttribute")
					continue;
				var field = type.FindField("Version");
				if (field == null || !field.IsLiteral || field.Constant == null)
					continue;
				var parsed = ExtractVersionText(field.Constant.Value as string);
				if (!string.IsNullOrEmpty(parsed))
					return parsed;
			}

			// 2) Assembly-level custom attributes mentioning Babel
			if (module.Assembly != null) {
				var v = ExtractVersionFromCustomAttributes(module.Assembly.CustomAttributes);
				if (!string.IsNullOrEmpty(v))
					return v;
			}

			// 3) Module-level custom attributes mentioning Babel
			var moduleVersion = ExtractVersionFromCustomAttributes(module.CustomAttributes);
			if (!string.IsNullOrEmpty(moduleVersion))
				return moduleVersion;

			// 4) Fallback: look at type names that embed a version token
			foreach (var type in module.GetTypes()) {
				var parsed = ExtractVersionText(type.FullName);
				if (!string.IsNullOrEmpty(parsed))
					return parsed;
			}

			return null;
		}

		static string ExtractVersionFromCustomAttributes(IList<CustomAttribute> attrs) {
			if (attrs == null)
				return null;
			foreach (var attr in attrs) {
				if (attr == null || attr.AttributeType == null)
					continue;
				var attrTypeName = attr.AttributeType.FullName;
				if (attrTypeName == null || attrTypeName.IndexOf("Babel", StringComparison.OrdinalIgnoreCase) < 0)
					continue;

				for (int i = 0; i < attr.ConstructorArguments.Count; i++) {
					var arg = attr.ConstructorArguments[i].Value as string;
					var parsed = ExtractVersionText(arg);
					if (!string.IsNullOrEmpty(parsed))
						return parsed;
				}

				foreach (var prop in attr.Properties) {
					var propVal = prop.Argument.Value as string;
					var parsed = ExtractVersionText(propVal);
					if (!string.IsNullOrEmpty(parsed))
						return parsed;
				}

				foreach (var field in attr.Fields) {
					var fieldVal = field.Argument.Value as string;
					var parsed = ExtractVersionText(fieldVal);
					if (!string.IsNullOrEmpty(parsed))
						return parsed;
				}
			}
			return null;
		}

		static string ExtractVersionText(string input) {
			if (string.IsNullOrEmpty(input))
				return null;
			var val4 = Regex.Match(input, @"(\d+\.\d+\.\d+\.\d+)");
			if (val4.Success)
				return val4.Groups[1].Value;
			var val3 = Regex.Match(input, @"(\d+\.\d+\.\d+)");
			if (val3.Success)
				return val3.Groups[1].Value;
			return null;
		}

		static Version ParseVersion(string text) {
			if (string.IsNullOrEmpty(text))
				return null;
			try {
				return new Version(text);
			}
			catch {
			}
			return null;
		}

		bool HasVmEncryptedMethodPattern() {
			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods) {
					if (method == null || method.Body == null)
						continue;
					var instrs = method.Body.Instructions;
					if (instrs.Count < 12)
						continue;
					if (!instrs[0].IsLdcI4())
						continue;
					if (instrs[1].OpCode != OpCodes.Newarr)
						continue;
					if (instrs[2].OpCode != OpCodes.Dup)
						continue;
					if (!instrs[3].IsLdcI4())
						continue;

					for (int i = instrs.Count - 1; i >= 0; i--) {
						var instr = instrs[i];
						if (instr.OpCode != OpCodes.Call)
							continue;
						var calledMethod = instr.Operand as MethodDef;
						if (calledMethod == null)
							continue;
						if (calledMethod.Parameters.Count == 4)
							return true;
					}
				}
			}
			return false;
		}

		bool HasFailFastStubPattern() {
			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods) {
					if (method == null || method.Body == null)
						continue;
					var instrs = method.Body.Instructions;
					if (instrs.Count != 3)
						continue;
					if (instrs[0].OpCode != OpCodes.Ldnull)
						continue;
					if (instrs[1].OpCode != OpCodes.Call)
						continue;
					if (instrs[2].OpCode != OpCodes.Ret)
						continue;
					if (instrs[1].Operand != null &&
						instrs[1].Operand.ToString().IndexOf("FailFast", StringComparison.OrdinalIgnoreCase) >= 0)
						return true;
				}
			}
			return false;
		}

		bool HasModuleBangMethodPattern() {
			foreach (var type in module.Types) {
				if (type == null || !type.IsGlobalModuleType)
					continue;
				foreach (var method in type.Methods) {
					if (method == null)
						continue;
					if (method.Name == "@!")
						return true;
				}
			}
			return false;
		}

		bool HasPrivateUseNameDensity() {
			int score = 0;
			foreach (var type in module.GetTypes()) {
				if (HasPrivateUseChar(type.Name))
					score++;
				foreach (var method in type.Methods) {
					if (HasPrivateUseChar(method.Name))
						score++;
				}
				foreach (var field in type.Fields) {
					if (HasPrivateUseChar(field.Name))
						score++;
				}
				if (score >= 20)
					return true;
			}
			return false;
		}

		static bool HasPrivateUseChar(string s) {
			if (string.IsNullOrEmpty(s))
				return false;
			for (int i = 0; i < s.Length; i++) {
				char c = s[i];
				if (c >= 0xE000 && c <= 0xF8FF)
					return true;
			}
			return false;
		}

		void CleanFailFastStubs() {
			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods) {
					if (method == null || method.Body == null)
						continue;
					var instrs = method.Body.Instructions;
					if (instrs.Count != 3)
						continue;
					if (instrs[0].OpCode != OpCodes.Ldnull || instrs[1].OpCode != OpCodes.Call || instrs[2].OpCode != OpCodes.Ret)
						continue;
					if (instrs[1].Operand == null || instrs[1].Operand.ToString().IndexOf("FailFast", StringComparison.OrdinalIgnoreCase) < 0)
						continue;
					instrs[0].OpCode = OpCodes.Nop;
					instrs[1].OpCode = OpCodes.Nop;
				}
			}
		}

		void CleanDelegateProxyCalls() {
			int candidates = 0;
			int resolved = 0;
			int replaced = 0;

			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods) {
					if (method == null || method.Body == null)
						continue;
					var instrs = method.Body.Instructions;
					for (int i = 0; i < instrs.Count; i++) {
						if (instrs[i].OpCode != OpCodes.Call && instrs[i].OpCode != OpCodes.Callvirt)
							continue;
						var proxyMethod = instrs[i].Operand as MethodDef;
						if (proxyMethod == null || proxyMethod.DeclaringType == null)
							continue;
						if (!proxyMethod.DeclaringType.IsDelegate)
							continue;
						if (proxyMethod.Name == "Invoke")
							continue;
						candidates++;

						Delegate resolvedDelegate;
						if (!TryResolveDelegate(proxyMethod.DeclaringType, out resolvedDelegate))
							continue;
						if (resolvedDelegate == null || resolvedDelegate.Method == null)
							continue;
						resolved++;

						try {
							MethodBase resolvedMethod = resolvedDelegate.Method;
							if (IsDynamicMethod(resolvedMethod)) {
								var dmReader = new DynamicMethodBodyReader(module, resolvedDelegate);
								if (!dmReader.Read())
									continue;
								var dmInstrs = dmReader.Instructions;
								if (dmInstrs == null || dmInstrs.Count < 2)
									continue;
								var patchedInstr = dmInstrs[dmInstrs.Count - 2];
								if (patchedInstr.OpCode == null)
									continue;
								instrs[i].OpCode = patchedInstr.OpCode;
								instrs[i].Operand = patchedInstr.Operand;
							}
							else {
								var imported = module.Import(resolvedMethod);
								if (imported == null)
									continue;
								instrs[i].OpCode = OpCodes.Call;
								instrs[i].Operand = imported;
							}
							replaced++;
						}
						catch {
						}
					}
				}
			}

			Logger.v("[*] Legacy delegate cleanup: candidates={0}, resolved={1}, replaced={2}", candidates, resolved, replaced);
			lastDelegateCandidates = candidates;
			lastDelegateResolved = resolved;
			lastDelegateReplaced = replaced;
		}

		bool TryResolveDelegate(TypeDef delegateTypeDef, out Delegate resolvedDelegate) {
			resolvedDelegate = null;
			if (runtimeAssembly == null || delegateTypeDef == null)
				return false;

			try {
				// Legacy Babel pattern used by Babel-DeobfuscatorNET4:
				// the second constructor body contains 5 or 6 instructions and initializes the static delegate field.
				var ctors = delegateTypeDef.FindConstructors().ToArray();
				if (ctors.Length > 1 && ctors[1] != null && ctors[1].Body != null) {
					var ctor = ctors[1];
					var cInstrs = ctor.Body.Instructions;
					if (cInstrs.Count == 5 || cInstrs.Count == 6) {
						var callInstr = cInstrs[cInstrs.Count - 2];
						var initMethodDef = callInstr.Operand as MethodDef;
						if (callInstr.OpCode == OpCodes.Call && initMethodDef != null) {
							var initMethod = runtimeAssembly.ManifestModule.ResolveMethod(initMethodDef.MDToken.ToInt32());
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

						var initMethod = runtimeAssembly.ManifestModule.ResolveMethod(initMethodDef.MDToken.ToInt32());
						if (initMethod == null)
							continue;
						var args = new object[count];
						for (int k = 0; k < count; k++)
							args[k] = cctorInstrs[start + k].GetLdcI4Value();
						initMethod.Invoke(null, args);
					}
				}

				try {
					var runtimeType = runtimeAssembly.ManifestModule.ResolveType(delegateTypeDef.MDToken.ToInt32());
					if (runtimeType != null)
						RuntimeHelpers.RunClassConstructor(runtimeType.TypeHandle);
				}
				catch {
				}

				foreach (var f in delegateTypeDef.Fields) {
					if (f == null || !f.IsStatic)
						continue;
					FieldInfo runtimeField = null;
					try {
						runtimeField = runtimeAssembly.ManifestModule.ResolveField(f.MDToken.ToInt32());
					}
					catch {
						runtimeField = null;
					}
					if (runtimeField == null)
						continue;

					Delegate candidate = null;
					try {
						candidate = runtimeField.GetValue(null) as Delegate;
					}
					catch {
						candidate = null;
					}
					if (candidate == null)
						continue;
					resolvedDelegate = candidate;
					return true;
				}

				return false;
			}
			catch {
				return false;
			}
		}

		static bool IsDynamicMethod(MethodBase method) {
			if (method == null)
				return false;
			var n = method.GetType().FullName;
			if (string.IsNullOrEmpty(n))
				return false;
			return n.IndexOf("DynamicMethod", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		void CleanIntegers() {
			CleanCalls(
				new MethodSigMatcher(module.CorLibTypes.Int32, new[] { module.CorLibTypes.Int32 }),
				OpCodes.Ldc_I4,
				1,
				args => ResolveAndInvokeInt((MethodDef)args[0], args[1]));
		}

		void CleanFloats() {
			CleanCalls(
				new MethodSigMatcher(module.CorLibTypes.Single, new[] { module.CorLibTypes.Int32 }),
				OpCodes.Ldc_R4,
				1,
				args => ResolveAndInvokeFloat((MethodDef)args[0], args[1]));
		}

		void CleanDoubles() {
			CleanCalls(
				new MethodSigMatcher(module.CorLibTypes.Double, new[] { module.CorLibTypes.Int32 }),
				OpCodes.Ldc_R8,
				1,
				args => ResolveAndInvokeDouble((MethodDef)args[0], args[1]));
		}

		void CleanStringsMethodOne() {
			CleanCalls(
				new MethodSigMatcher(module.CorLibTypes.String, new[] { module.CorLibTypes.Int32 }),
				OpCodes.Ldstr,
				1,
				args => ResolveAndInvokeString1((MethodDef)args[0], args[1]));
		}

		void CleanStringsMethodTwo() {
			CleanCalls(
				new MethodSigMatcher(module.CorLibTypes.String, new[] { module.CorLibTypes.String, module.CorLibTypes.Int32 }),
				OpCodes.Ldstr,
				2,
				args => ResolveAndInvokeString2((MethodDef)args[0], args[1], args[2]));
		}

		delegate object CallHandler(object[] args);

		void CleanCalls(MethodSigMatcher sigMatcher, OpCode replacementOpCode, int argCount, CallHandler callHandler) {
			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods) {
					if (method == null || method.Body == null)
						continue;
					for (int i = 0; i < method.Body.Instructions.Count; i++) {
						var instr = method.Body.Instructions[i];
						if (instr.OpCode != OpCodes.Call)
							continue;
						var calledMethod = instr.Operand as MethodDef;
						if (calledMethod == null || !calledMethod.IsStatic)
							continue;
						if (!sigMatcher.Matches(calledMethod))
							continue;
						if (i < argCount)
							continue;

						object value = null;
						try {
							var args = new object[argCount + 1];
							args[0] = calledMethod;
							if (argCount == 1) {
								var prev = method.Body.Instructions[i - 1];
								if (!prev.IsLdcI4())
									continue;
								args[1] = prev.GetLdcI4Value();
							}
							else if (argCount == 2) {
								var prev1 = method.Body.Instructions[i - 1];
								var prev2 = method.Body.Instructions[i - 2];
								if (!prev1.IsLdcI4())
									continue;
								if (prev2.OpCode != OpCodes.Ldstr)
									continue;
								args[1] = prev2.Operand as string;
								args[2] = prev1.GetLdcI4Value();
							}
							value = callHandler(args);
						}
						catch {
							continue;
						}
						if (value == null)
							continue;

						method.Body.Instructions[i].OpCode = OpCodes.Nop;
						method.Body.Instructions[i - 1].OpCode = replacementOpCode;
						method.Body.Instructions[i - 1].Operand = value;
						if (argCount == 2)
							method.Body.Instructions[i - 2].OpCode = OpCodes.Nop;
					}
				}
			}
		}

		object ResolveAndInvokeInt(MethodDef method, object arg0) {
			var mb = ResolveMethod(method);
			if (mb == null)
				return null;
			return mb.Invoke(null, new object[] { (int)arg0 });
		}

		object ResolveAndInvokeFloat(MethodDef method, object arg0) {
			var mb = ResolveMethod(method);
			if (mb == null)
				return null;
			return mb.Invoke(null, new object[] { (int)arg0 });
		}

		object ResolveAndInvokeDouble(MethodDef method, object arg0) {
			var mb = ResolveMethod(method);
			if (mb == null)
				return null;
			return mb.Invoke(null, new object[] { (int)arg0 });
		}

		object ResolveAndInvokeString1(MethodDef method, object arg0) {
			var mb = ResolveMethod(method);
			if (mb == null)
				return null;
			return mb.Invoke(null, new object[] { (int)arg0 });
		}

		object ResolveAndInvokeString2(MethodDef method, object arg0, object arg1) {
			var mb = ResolveMethod(method);
			if (mb == null)
				return null;
			return mb.Invoke(null, new object[] { (string)arg0, (int)arg1 });
		}

		MethodBase ResolveMethod(MethodDef method) {
			if (runtimeAssembly == null || method == null)
				return null;
			try {
				return runtimeAssembly.ManifestModule.ResolveMethod(method.MDToken.ToInt32());
			}
			catch {
				return null;
			}
		}

		class MethodSigMatcher {
			readonly TypeSig retType;
			readonly TypeSig[] argTypes;

			public MethodSigMatcher(TypeSig retType, TypeSig[] argTypes) {
				this.retType = retType;
				this.argTypes = argTypes;
			}

			public bool Matches(MethodDef method) {
				if (method == null || method.MethodSig == null || method.Parameters.Count != argTypes.Length)
					return false;
				if (!new SigComparer().Equals(method.ReturnType, retType))
					return false;
				for (int i = 0; i < argTypes.Length; i++) {
					if (!new SigComparer().Equals(method.Parameters[i].Type, argTypes[i]))
						return false;
				}
				return true;
			}
		}
	}
}
