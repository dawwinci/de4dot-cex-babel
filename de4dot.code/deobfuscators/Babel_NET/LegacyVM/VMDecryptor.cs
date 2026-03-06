using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using de4dot.code.deobfuscators.Babel_NET.LegacyVM;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using System.Reflection.Emit;

namespace de4dot.code.deobfuscators.Babel_NET.LegacyVM
{
	public class TypeConflictResolver {
		Dictionary<string, System.Reflection.Emit.TypeBuilder> builders =
			new Dictionary<string, System.Reflection.Emit.TypeBuilder>();

		public void Bind(AppDomain domain) {
			domain.TypeResolve += Domain_TypeResolve;
		}

		public void AddTypeBuilder(System.Reflection.Emit.TypeBuilder builder) {
			if (builder == null || string.IsNullOrEmpty(builder.Name))
				return;
			builders[builder.Name] = builder;
		}

		Assembly Domain_TypeResolve(object sender, ResolveEventArgs args) {
			return null;
		}
	}
	
	
	public struct Entity : IEquatable<Entity>
	{
		public bool Equals(Entity other)
		{
			throw new NotImplementedException("Your equality check here...");
		}

		public override bool Equals(object obj)
		{
			if (obj == null || !(obj is Entity))
				return false;

			return Equals((Entity)obj);
		}

		public static bool operator ==(Entity e1, Entity e2)
		{
			return e1.Equals(e2);
		}

		public static bool operator !=(Entity e1, Entity e2)
		{
			return !(e1 == e2);
		}

		public override int GetHashCode()
		{
			return 1;
			// throw new NotImplementedException("Your lightweight hashing algorithm, consistent with Equals method, here...");
		}
	}
	
	
	public class VMDecryptor
	{
		static Assembly runtimeAssembly;

		public static void run(ModuleDefMD module, Assembly asm)
		{
			runtimeAssembly = asm;
			Testing(asm);
			
			VMDecryptor.FindEncryptedMethods(module);
			if (VMDecryptor.allEncMethods.Count != 0)
			{
				DecryptionMethods dec = VMDecryptor.setUpDecryptionRoutine(VMDecryptor.allEncMethods[0].method);
				VMDecryptor.DecryptMethods(dec, asm, module);
			}
		}
		
		static Type NoGeneric = null;
		internal static void GetNoGeneric(Type type, Assembly asm)
		{
			
			if (type==null||NoGeneric!=null)
				return;

			List<Type> derived = GetDerivedTypes(type, asm);
			if (derived!=null&&derived.Count>0)
			{
				foreach (Type der1 in derived)
				{
					if (!der1.ContainsGenericParameters)
					{
						NoGeneric = der1;
						return;
					}
					
					if (NoGeneric!=null)
						return;
					
					GetNoGeneric(der1, asm);
				}
			}
			
		}
		
		static bool ShouldCallEmit = true;
		
		internal static Type GetContrainedType(Type type, Assembly asm)
		{
			
			bool IsSelf = false;
			if (!type.IsGenericParameter&&type.ContainsGenericParameters)
			{
				Type[] genpars = type.GetGenericArguments();
				Type gentype = type.GetGenericTypeDefinition();
								
				Type[] resolved = new Type[genpars.Length];
				for (int i=0;i<resolved.Length;i++)
				{
					
					IsSelf = false;
					if (replacements.Count>0&&genpars[i].IsGenericParameter)
					{
				
						Type[] ccontrains = genpars[i].GetGenericParameterConstraints();
						for (int k=0;k<ccontrains.Length;k++)
						{
						
							if (replacements.ContainsKey(ccontrains[k]))
							{
								resolved[i] =  (Type)replacements[ccontrains[k]];
								IsSelf = true;
							}
						
						}
					  
					}
						if (IsSelf) continue;
					
					resolved[i] = GetContrainedType(genpars[i], asm);
				}

				
				Type constructedClass = gentype.MakeGenericType(resolved);
				return constructedClass;
			}

			if (!type.IsGenericParameter)
			{
				return type;
			}
			
			Type[] contrains = type.GetGenericParameterConstraints();
			if (contrains==null||contrains.Length==0)
				return  typeof(Object);
			else
			{
				
					if (replacements.Count>0)
					{
				
						for (int k=0;k<contrains.Length;k++)
						{
						
							if (replacements.ContainsKey(contrains[k]))
							{
								return replacements[contrains[k]];
							}
						
						}
					  
					}

				if (contrains.Length==1&&type.Assembly.GetName().Name=="mscorlib")
				{
					return contrains[0];
				}
				
				if (contrains.Length==1&&contrains[0].UnderlyingSystemType!=null&&
				    contrains[0].Name.StartsWith("IEquatable")&&contrains[0].UnderlyingSystemType.Namespace=="System"&&
				    contrains[0].ContainsGenericParameters&&contrains[0].GetGenericArguments().Length==1)
				{
					return typeof(Entity);
				}
				
				List<Type> gpContrains = new List<Type>();
				bool IsNewContrain = false;
				
				foreach (Type contrain in contrains)
				{
					if (contrain.ContainsGenericParameters)
					{
						gpContrains.Add(contrain);
					}
					

				}
				
				GenericParameterAttributes constraints = type.GenericParameterAttributes &
					GenericParameterAttributes.SpecialConstraintMask;
				
				if ((constraints & GenericParameterAttributes.DefaultConstructorConstraint) != 0)
					IsNewContrain = true;
				
				if (IsNewContrain)
				{
					ShouldCallEmit = true;
				}
				
				if (gpContrains!=null&&gpContrains.Count>0)
				{

					if (gpContrains.Count==1)
					{
						NoGeneric = null;
						GetNoGeneric(gpContrains[0], asm);
						if (NoGeneric!=null)
						{
							//EmitedType = NoGeneric;
							//return NoGeneric;
						}
					}
					/*
					List<Type> GetDerivedTypes(Type gtype, Assembly asm)
					
					Type newDerived = gpContrain;

					while (newDerived!=null&&newDerived.ContainsGenericParameters)
					{
						newDerived = GetDerivedType(newDerived, true, asm);
						
						Console.WriteLine((newDerived.MetadataToken).ToString("X8")+"-"+newDerived.ToString());
											
						if (newDerived!=null&&!newDerived.ContainsGenericParameters)
							return newDerived;
					}
					 */
					/*Type Derived1 = GetDerivedType(gpContrain, false, asm);
					if (Derived1!=null)
						return Derived1;
					 */
					if (ShouldCallEmit)
					{
						ShouldCallEmit = false;
						EmitedType = EmitExtendsType(gpContrains, asm);
						return EmitedType;
					}
					else
						return gpContrains[0];
					
					
				}
				
				

				for (int k=0;k<contrains.Length;k++)
				{
					if (contrains[k].Equals(typeof(ValueType)))
						return typeof(int);
				}
				
				List<Type> interfaceContrain = new List<Type>();
				List<Type> classContrain = new List<Type>();
				for (int l=0;l<contrains.Length;l++)
				{
					if (contrains[l].IsInterface)
						interfaceContrain.Add(contrains[l]);
					else
						classContrain.Add(contrains[l]);
					
				}
				Type propertype = null;
				Type[] modTypes = asm.ManifestModule.GetTypes();
				for (int l=0;l<modTypes.Length;l++)
				{
					int PInterfacesCount = 0;
					
					if (classContrain.Count==0||(modTypes[l].BaseType!=null&&modTypes[l].BaseType.Equals(classContrain[0])))
					{
						if (interfaceContrain.Count==0)
						{
							propertype = modTypes[l];
							break;
						}
						Type[] implinterfaces = modTypes[l].GetInterfaces();
						if  (implinterfaces.Length>=interfaceContrain.Count)
						{
							for (int m=0;m<implinterfaces.Length;m++)
							{
								foreach (Type rinterface in interfaceContrain)
									if (implinterfaces[m].Equals(rinterface))
										PInterfacesCount++;

								if (PInterfacesCount==interfaceContrain.Count) break;
							}
							
							
							if (PInterfacesCount==interfaceContrain.Count)
							{
								propertype = modTypes[l];
								break;
							}
						}
					}
				}
				if (propertype!=null)
				{
					if (propertype.IsAbstract)
					{

						modTypes = asm.ManifestModule.GetTypes();
						for (int l=0;l<modTypes.Length;l++)
						{
							if (modTypes[l].BaseType.Equals(propertype))
							{
								propertype = modTypes[l];
								break;
							}
						}
					}
					
					return propertype;
				}
				else if (contrains.Length==1)
				{
					return contrains[0];
				}
				
				return typeof(Object);
			}
		}
		
		static Type[] modTypes = null;
		internal static Type GetDerivedType(Type gtype, bool PreferNoGeneric, Assembly asm)
		{
			if (gtype==null) return null;
			
			if (modTypes==null||modTypes.Length<=0)
				modTypes = asm.ManifestModule.GetTypes();
			
			List<Type> derivedTypes = new List<Type>();
			for (int l=0;l<modTypes.Length;l++)
			{
				// (modTypes[l].BaseType.GetGenericTypeDefinition()!=null&&
				//  modTypes[l].BaseType.GetGenericTypeDefinition().Equals(gtype))||
				
				if (modTypes[l].BaseType!=null&&(modTypes[l].BaseType.Equals(gtype)||
				                                 (modTypes[l].BaseType.Module!=null&&gtype.Module==modTypes[l].BaseType.Module&&
				                                  modTypes[l].BaseType.MetadataToken==gtype.MetadataToken)))
				{
					if (!PreferNoGeneric)
						return modTypes[l];
					
					if (!modTypes[l].ContainsGenericParameters)
						return modTypes[l];
					
					derivedTypes.Add(modTypes[l]);
				}
			}
			
			if (derivedTypes.Count>0)
				return derivedTypes[0];
			
			return null;
		}
		
		
		internal static List<Type> GetDerivedTypes(Type gtype, Assembly asm)
		{
			if (gtype==null) return null;
			
			if (modTypes==null||modTypes.Length<=0)
				modTypes = asm.ManifestModule.GetTypes();
			
			List<Type> derivedTypes = new List<Type>();
			for (int l=0;l<modTypes.Length;l++)
			{
				// (modTypes[l].BaseType.GetGenericTypeDefinition()!=null&&
				//  modTypes[l].BaseType.GetGenericTypeDefinition().Equals(gtype))||
				
				if (modTypes[l].BaseType!=null&&(modTypes[l].BaseType.Equals(gtype)||
				                                 (modTypes[l].BaseType.Module!=null&&gtype.Module==modTypes[l].BaseType.Module&&
				                                  modTypes[l].BaseType.MetadataToken==gtype.MetadataToken)))
				{
					
					derivedTypes.Add(modTypes[l]);
				}
			}
			
			for (int l=0;l<modTypes.Length;l++)
			{
				Type[] interfaces = modTypes[l].GetInterfaces();
				if (interfaces==null||interfaces.Length==0)
					continue;
				
				if (modTypes[l].MetadataToken==0x02000342)
				{
					
				}
				
				for (int m=0;m<interfaces.Length;m++)
				{  // .GetGenericTypeDefinition()
					if (interfaces[m].MetadataToken==gtype.MetadataToken&&
					    interfaces[m].Module==gtype.Module)
						derivedTypes.Add(modTypes[l]);
				}
				

			}
			
			return derivedTypes;
		}
		
		static Type EmitedType = null;
		
		static List<MethodInfo> toimplement;
		
		public static void GetMethodImplemnetation(Type type)
		{
			
			MethodInfo[] methods = type.GetMethods();
			foreach (MethodInfo method in methods)
				toimplement.Add(method);

			Type[] interfaces = type.GetInterfaces();
			foreach (Type interf in interfaces)
				GetMethodImplemnetation(interf);
			
		}
		
		public static bool IsNumberType(Type tname)
		{
			if (tname.FullName=="System.Int32")
				return true;
			
			return false;
		}
		
		public static PropertyInfo GetProperty(MethodInfo method)
		{
			PropertyInfo[] props = method.DeclaringType.GetProperties();
			if (props==null||props.Length==0)
				return null;
			
			foreach (PropertyInfo prop in props)
			{
				if (prop.GetGetMethod(true)==method)
					return prop;
				
				if (prop.GetSetMethod(true)==method)
					return prop;
			}
			
			return null;
			
		}
		
		public enum MethodType
		{
			None,
			GetMethod,
			SetMethod
		}
		
		public static System.Reflection.Emit.MethodBuilder EmitMethod(MethodInfo method,MethodType mtype, System.Reflection.Emit.TypeBuilder ivTypeBld)
		{
			ParameterInfo[] paramInfos = method.GetParameters();

			System.Reflection.MethodAttributes methodAttrib = System.Reflection.MethodAttributes.Public;
			
			if (mtype== MethodType.None)
				methodAttrib = System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Virtual | System.Reflection.MethodAttributes.Final;
			else if (mtype==MethodType.GetMethod)
				methodAttrib = System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Virtual | System.Reflection.MethodAttributes.Final |
					System.Reflection.MethodAttributes.NewSlot |
					System.Reflection.MethodAttributes.SpecialName|System.Reflection.MethodAttributes.HideBySig;
			
			System.Reflection.Emit.MethodBuilder methodBuilder = ivTypeBld.DefineMethod(method.Name,
			                                                                            methodAttrib,
			                                                                            CallingConventions.HasThis,
			                                                                            method.ReturnType,
			                                                                            method.ReturnParameter.GetRequiredCustomModifiers(),      // *
			                                                                            method.ReturnParameter.GetOptionalCustomModifiers(),      // *
			                                                                            paramInfos.Select(pi => pi.ParameterType).ToArray(),
			                                                                            paramInfos.Select(pi => pi.GetRequiredCustomModifiers()).ToArray(), // *
			                                                                            paramInfos.Select(pi => pi.GetOptionalCustomModifiers()).ToArray()  // *
			                                                                           );
			
			System.Reflection.Emit.ILGenerator ILgen = methodBuilder.GetILGenerator();
			var aka = method.ReturnType;
			if (method.ReturnType.Name!="Void")
			{
				if (IsNumberType(method.ReturnType))
					ILgen.Emit(System.Reflection.Emit.OpCodes.Ldc_I4_0);
				else
					ILgen.Emit(System.Reflection.Emit.OpCodes.Ldnull);
			}
			
			ILgen.Emit(System.Reflection.Emit.OpCodes.Ret);
			
			return methodBuilder;
		}
				
		public static Dictionary<Type, Type> replacements = new Dictionary<Type, Type>();
    
        public static System.Reflection.Emit.ModuleBuilder EmitModule = null;
        public static System.Reflection.Emit.AssemblyBuilder myAsmBuilder = null;
        public static TypeConflictResolver resolver;

        
       public static Random rdm = null;
private static string RandomHexString()
{
    // 64 character precision or 256-bits

    if (rdm==null)
    rdm = new Random();
        
    string hexValue = string.Empty;
    int num;

    for (int i = 0; i < 8; i++)
    {
        num = rdm.Next(0, int.MaxValue);
        hexValue += num.ToString("X8");
    }

    return hexValue;
}
        
		public static Type EmitExtendsType(List<Type> itypes, Assembly asm)
		{
			ShouldCallEmit = true;
						
			Console.WriteLine("No instance for type "+itypes[0].MetadataToken.ToString("X8")+" "+itypes[0].ToString());
			
			if (EmitModule==null)
			{
			AppDomain myDomain = AppDomain.CurrentDomain;
			AssemblyName myAsmName = new AssemblyName();
			myAsmName.Name = "EmitedAsm";
			
			System.Reflection.Emit.AssemblyBuilder myAsmBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(
				myAsmName,
				System.Reflection.Emit.AssemblyBuilderAccess.Run);

				EmitModule = myAsmBuilder.DefineDynamicModule(myAsmName.Name);//,
			                                              //myAsmName.Name+".dll");
			
			

			}
			
			if (resolver==null)
			{
			resolver = new TypeConflictResolver();
			resolver.Bind(AppDomain.CurrentDomain);
			}
			
			string additional = itypes[0].ToString()+RandomHexString();
			System.Reflection.Emit.TypeBuilder ivTypeBld = EmitModule.DefineType("EmitedType"+additional,
			                                                              System.Reflection.TypeAttributes.Public);
			
			if (ThisType!=null&&replacements.ContainsKey(ThisType))
			{
				Console.WriteLine("WTF?");
			}
			
			if (ThisType!=null&&!replacements.ContainsKey(ThisType))
				replacements.Add(ThisType, ivTypeBld);
			
            resolver.AddTypeBuilder(ivTypeBld);
          
			System.Reflection.Emit.ConstructorBuilder ctor0 = ivTypeBld.DefineConstructor(
				System.Reflection.MethodAttributes.Public,
				CallingConventions.Standard,
				Type.EmptyTypes);
			
			Type RBaseType = null;			
			List<Type> interfaces = new List<Type>();
			foreach (Type itype in itypes)
			{
				if (itype.IsInterface)
				{
					interfaces.Add(itype);
					
					//Type aka = EmitExtendsType(itype, asm);
					ShouldCallEmit = false;
					Type contrain1 = GetContrainedType(itype,  asm);
					ShouldCallEmit = true;
					ivTypeBld.AddInterfaceImplementation(contrain1);
				}
				else
				{
					RBaseType = itype;
					ivTypeBld.SetParent(itype);
				}
				
				
				
			if (replacements.Count>0)
			{
			Type[] genericArgs = itype.GetGenericArguments();
			
				for (int i=0;i<genericArgs.Length;i++)
				{
				if (genericArgs[i].IsGenericParameter)
				{
					Type[] ccontrains = genericArgs[i].GetGenericParameterConstraints();
						for (int k=0;k<ccontrains.Length;k++)
						{
						
							if (replacements.ContainsKey(ccontrains[k]))
							{
							
							//replacements.Remove(ccontrains[k]);
							Type backup = ThisType;
							ThisType = null;
							List<Type> newtypes = new List<Type>();
							newtypes.Add(ccontrains[0]);
							//Type NewType = EmitExtendsType(newtypes, asm);
							//ThisType = backup;
							//ivTypeBld.SetParent((Type)replacements[ccontrains[k]]);
							
							}
						
						}
				}
				}
			
			}
			
				
			}
			

			
			ConstructorInfo ParameterlessCtor = null;
			Type GenericType = null;
			if (RBaseType!=null)
			{
				Type[] genericArgs = RBaseType.GetGenericArguments();
				Type[] rtypes = new Type[genericArgs.Length];
				
				bool ContainBaseType = false;
				bool SelfReference = false;
				for (int i=0;i<rtypes.Length;i++)
				{
					SelfReference = false;
					if (genericArgs[i].IsGenericParameter)
					{
					
					Type[] ccontrains = genericArgs[i].GetGenericParameterConstraints();
						for (int k=0;k<ccontrains.Length;k++)
						{
						
							if (replacements.ContainsKey(ccontrains[k]))
							{
								SelfReference = true;
								rtypes[i] =  (Type)replacements[ccontrains[k]];
							}
						
						}
					
					}
					
					if (SelfReference) continue;
					
					ShouldCallEmit = true;
					rtypes[i] =  GetContrainedType(genericArgs[i], asm);
					ShouldCallEmit = false;
					if (rtypes[i].Equals(RBaseType))
					{
						ContainBaseType = true;
						rtypes[i] =  (Type)ivTypeBld;
					}
				}
				
				Type CBaseType = asm.ManifestModule.ResolveType(RBaseType.MetadataToken);
				GenericType = CBaseType.MakeGenericType(rtypes);
				
				ConstructorInfo[] constructors = CBaseType.GetConstructors();

				foreach (ConstructorInfo contr in constructors)
				{
					if (contr.GetParameters().Length==0)
					{
						ParameterlessCtor = contr;
						break;
					}

				}
			}
			
			

			
			/*
			if (ContainBaseType)
			{
			for (int i=0;i<rtypes.Length;i++)
			{
				rtypes[i] =  (Type)ivTypeBld;
			}
			}
			 */
			
			System.Reflection.Emit.ILGenerator ctor0IL = ctor0.GetILGenerator();
			
			//ConstructorInfo co1 = RBaseType.GetConstructor(
			//	BindingFlags.Instance | BindingFlags.Public, null,
			//	CallingConventions.HasThis, Type.EmptyTypes, null);
			
			//ConstructorInfo co2 = System.Reflection.Emit.TypeBuilder.GetConstructor(GenricType, co1);
			ConstructorInfo ci = null;
			
			if (ParameterlessCtor!=null)
			{
				ci = System.Reflection.Emit.TypeBuilder.GetConstructor(GenericType, ParameterlessCtor);
				//ci = ParameterlessCtor;
			}
			else
			{
				ci = typeof(object).GetConstructor(Type.EmptyTypes);
			}
			
			ctor0IL.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
			ctor0IL.Emit(System.Reflection.Emit.OpCodes.Call, ci);
			ctor0IL.Emit(System.Reflection.Emit.OpCodes.Ret);
			// https://stackoverflow.com/questions/56564992/when-implementing-an-interface-that-has-a-method-with-in-parameter-by-typebuil
			
			toimplement = new List<MethodInfo>();
			foreach (Type inter in interfaces)
			{
				GetMethodImplemnetation(inter);
			}
			
			List<PropertyInfo> toimpProps = new List<PropertyInfo>();
			
			foreach (MethodInfo method in toimplement)
			{
				
				PropertyInfo prop = GetProperty(method);
				if (prop!=null)
				{
					if (!toimpProps.Contains(prop))
						toimpProps.Add(prop);
				}
				else
				{
					EmitMethod(method, MethodType.None, ivTypeBld);

				}
				
			}
			
			foreach (PropertyInfo prop in toimpProps)
			{
				System.Reflection.Emit.PropertyBuilder custPropBldr = ivTypeBld.DefineProperty(prop.Name,
				                                                                               prop.Attributes,
				                                                                               prop.PropertyType,
				                                                                               null);
				
				if (prop.GetGetMethod(true)!=null)
				{
					System.Reflection.Emit.MethodBuilder NewGetMethod = EmitMethod(prop.GetGetMethod(true),MethodType.GetMethod, ivTypeBld);
					custPropBldr.SetGetMethod(NewGetMethod);
				}
				
				
			}
			
			Type ctype = ivTypeBld.CreateType();
			
			//myAsmBuilder.Save("fucker.dll");
			

			//object instance1 = Activator.CreateInstance(ctype);
			
			ShouldCallEmit = true;
			return ivTypeBld;
			
			//return ctype;
			//return Activator.CreateInstance(ctype);
			
		}
		
		public static Type ThisType = null;
		internal static object GetGenericInstance(Type gtype, Assembly asm)
		{
			if (gtype==null) return null;
			
			Type constructedClass = null;
			Type[] genericArgs = gtype.GetGenericArguments();
			if (genericArgs==null||genericArgs.Length==0)
			{
				return MyCreateInstance(gtype, asm);
			}
			
			
			if (gtype.MetadataToken==0x02000336)
			{  // 33555194 (0x020002FA)

				//Console.WriteLine(ConstrMinO.GetParameters()[0].ParameterType.IsInterface.ToString());
				//Console.WriteLine(ConstrMinO.GetParameters()[0].ParameterType.ToString());
			}
			
			if (gtype.IsInterface)
			{
				
			}
			
			ThisType = gtype;
			Type[] rtypes = new Type[genericArgs.Length];
			for (int i=0;i<rtypes.Length;i++)
			{
				EmitedType = null;
				rtypes[i] =  GetContrainedType(genericArgs[i], asm);
				//object instance1 = null;
				//if (EmitedType!=null)
				//{
					
					//constructedClass = EmitedType;
					//goto InstatiateClass;
					//instance1 = Activator.CreateInstance(EmitedType);
					
					
					//return instance1;
				//}
				
				//return Activator.CreateInstance(EmitedType);
				
				//if (AditionalGenericArgsContrain[i]!=null)
				//rtypes[i] = AditionalGenericArgsContrain[i];
				
			}
			ThisType = null;
			replacements.Clear();
						
			//Type fuck1 = null;
			if (rtypes.Length>2)
			{
			
			bool is1 = rtypes[0] is System.Reflection.Emit.TypeBuilder;
			//rtypes[0] = ((System.Reflection.Emit.TypeBuilder)rtypes[0]).CreateType();
			bool is2 = rtypes[3] is System.Reflection.Emit.TypeBuilder;
			//rtypes[3] = ((System.Reflection.Emit.TypeBuilder)rtypes[3]).CreateType();
			//fuck1 = rtypes[03].BaseType;
			}
			
			constructedClass = gtype.MakeGenericType(rtypes);			
			ConstructorInfo[] constructors = constructedClass.GetConstructors();
			ConstructorInfo ConstrMin = null;
			bool HasParameterless = false;
			foreach (ConstructorInfo contr in constructors)
			{
				if (contr.GetParameters().Length==0)
				{
					HasParameterless = true;
					break;
				}
				else if (ConstrMin==null)
					ConstrMin = contr;
				else if (ConstrMin!=null&&contr.GetParameters().Length<ConstrMin.GetParameters().Length)
				{
					ConstrMin = contr;
				}
			}
			if (HasParameterless)
				return Activator.CreateInstance(constructedClass);
			
			ParameterInfo[] parameters = ConstrMin.GetParameters();
			object[] constructorParam = new object[parameters.Length];
			
			for (int i=0;i<constructorParam.Length;i++)
			{
				if (parameters[i].ParameterType.ContainsGenericParameters)
					constructorParam[i] = GetGenericInstance(parameters[i].ParameterType, asm);
				else
					constructorParam[i] = MyCreateInstance(parameters[i].ParameterType, asm);

			}
			
			try
			{
				return ConstrMin.Invoke(constructorParam);
			}
			catch
			{
				object instance = null;
				if (constructedClass.Module==runtimeAssembly.ManifestModule)
				{
					try
					{
						instance = FormatterServices.GetUninitializedObject(constructedClass); //does not call ctor
						if (instance!=null)
							return instance;
					}
					catch
					{
						
					}
				}
				return null;
			}

		}
		
		private static MethodBase GetMethod(Type decl, MethodBase methodBase_r)
		{
			
			PropertyInfo[] properties = decl.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			ParameterInfo[] methodBase_r_param = methodBase_r.GetParameters();
			foreach (PropertyInfo pinfo in properties)
			{
				if (pinfo.CanRead&&pinfo.GetGetMethod(true).Name==methodBase_r.Name&&
				    methodBase_r_param.Length==pinfo.GetGetMethod(true).GetParameters().Length)
				{
					return pinfo.GetGetMethod(true);
				}
				
				if (pinfo.CanWrite&&pinfo.GetSetMethod(true).Name==methodBase_r.Name&&
				    methodBase_r_param.Length==pinfo.GetSetMethod(true).GetParameters().Length)
				{
					return pinfo.GetSetMethod(true);
				}
			}
			
			MethodInfo[] methods = decl.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			
			foreach (MethodInfo minfo in methods)
			{
				if (minfo.Name==methodBase_r.Name)
				{
					ParameterInfo[] minfo_param = minfo.GetParameters();
					if (minfo_param.Length==0&&methodBase_r_param.Length==0)
					{
						return minfo;
					}
					
					if (minfo_param.Length==methodBase_r_param.Length)
					{
						bool AreEqual = true;
						for (int i=0;i<minfo_param.Length;i++)
						{
							if (minfo_param[i].ParameterType!=methodBase_r_param[i].ParameterType&&!methodBase_r_param[i].ParameterType.ContainsGenericParameters)
							{
								AreEqual = false;
								break;
							}
						}
						
						if (AreEqual)
							return minfo;
						
					}
					
				}
			}
			
			return null;
		}
		
		private static MethodBase GetConstructor(Type decl, MethodBase methodBase_r)
		{
			ConstructorInfo[] constructors = decl.GetConstructors(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			
			foreach (ConstructorInfo cinfo in constructors)
			{
				if (cinfo.Name==methodBase_r.Name)
				{
					ParameterInfo[] minfo_param = cinfo.GetParameters();
					ParameterInfo[] methodBase_r_param = methodBase_r.GetParameters();
					if (minfo_param.Length==0&&methodBase_r_param.Length==0)
					{
						return cinfo;
					}
					
					if (minfo_param.Length==methodBase_r_param.Length)
					{
						bool AreEqual = true;
						for (int i=0;i<minfo_param.Length;i++)
						{
							if (minfo_param[i].ParameterType!=methodBase_r_param[i].ParameterType&&!methodBase_r_param[i].ParameterType.ContainsGenericParameters)
							{
								AreEqual = false;
								break;
							}
						}
						
						if (AreEqual)
							return cinfo;
						
					}
					
				}
			}
			
			return null;
		}
		
		public static Type GetImplementedInterface(Type itype, Assembly asm, ref Type interf)
		{
			//Console.WriteLine(type.MetadataToken.ToString("X8")+"-"+type.ToString());
			
			if (modTypes==null||modTypes.Length<=0)
				modTypes = asm.ManifestModule.GetTypes();
			
			Type InterfaceImlem = null;
			for (int l=0;l<modTypes.Length;l++)
			{
				Type[] interfaces = modTypes[l].GetInterfaces();
				for (int m=0;m<interfaces.Length;m++)
				{
					if (interfaces[m].Equals(itype)||
					    (interfaces[m].Module!=null&&interfaces[m].Module==itype.Module&&
					     interfaces[m].MetadataToken==itype.MetadataToken))
					{
						interf = interfaces[m];
						InterfaceImlem = modTypes[l];
						break;
					}
				}
				if (InterfaceImlem!=null)
					break;

			}
			//	modTypes[l].BaseType.Module!=null&&gtype.Module==modTypes[l].BaseType.Module&&
			//	                                  modTypes[l].BaseType.MetadataToken==gtype.MetadataToken))
			//Console.WriteLine(InterfaceImlem.MetadataToken.ToString("X8")+"-"+InterfaceImlem.ToString());
			return InterfaceImlem;
		}
		
		public unsafe static object MyCreateInstance(Type itype, Assembly asm)
		{
			if (itype==null) return null;
			
			Type type = itype;
			if (type.IsInterface)
			{
				Type implIterface = null;
				Type InterfaceDerived = GetImplementedInterface(itype, asm, ref implIterface);

				if (InterfaceDerived!=null)
					type = InterfaceDerived;
				
			}
			else if (type.IsAbstract)
			{
				
				if (modTypes==null||modTypes.Length<=0)
					modTypes = asm.ManifestModule.GetTypes();
				
				for (int l=0;l<modTypes.Length;l++)
				{
					if (modTypes[l].BaseType!=null&&modTypes[l].BaseType.Equals(type))
					{
						type = modTypes[l];
						break;
					}
				}
				
			}
			
			if (type==null||type.IsAbstract)
			{
				
			}
			
			if (type.IsPointer)
			{
				Type ptrtype = type.GetElementType();
				object instance = MyCreateInstance(ptrtype, asm);
				GCHandle handle = GCHandle.Alloc(instance, GCHandleType.Normal);
				handles.Add(handle);
				object ptrinstance = Pointer.Box((void*)GCHandle.ToIntPtr(handle), type);
				return ptrinstance;
			}
			
			// https://stackoverflow.com/questions/390578/creating-instance-of-type-without-default-constructor-in-c-sharp-using-reflectio
			if (type.Module==runtimeAssembly.ManifestModule)
				return FormatterServices.GetUninitializedObject(type); //does not call ctor
			
			ConstructorInfo[] constructors = type.GetConstructors(BindingFlags.Public |
			                                                      BindingFlags.NonPublic | BindingFlags.Instance);
			if (constructors.Length==0)
			{

				try
				{
					return Activator.CreateInstance(type);
				}
				catch(Exception exc)
				{
					Console.WriteLine(type.Assembly.ToString());
					Console.WriteLine(type.ToString());
					Console.WriteLine(exc.ToString());
				}
			}
			
			ConstructorInfo ConstrMin = null;
			ConstructorInfo ConstrNoParam = null;
			bool HasParameterless = false;
			
			foreach (ConstructorInfo contr in constructors)
			{
				if (contr.GetParameters().Length==0)
				{
					ConstrNoParam = contr;
					HasParameterless = true;
					break;
				}
				else if (ConstrMin==null)
					ConstrMin = contr;
				else if (ConstrMin!=null&&contr.GetParameters().Length<ConstrMin.GetParameters().Length)
				{
					ConstrMin = contr;
				}
			}
			
			if (HasParameterless)
				return Activator.CreateInstance(type);
			
			ParameterInfo[] parameters = ConstrMin.GetParameters();
			object[] constructorParam = new object[parameters.Length];
			
			for (int i=0;i<constructorParam.Length;i++)
			{
				if (!parameters[i].ParameterType.IsGenericParameter)
					constructorParam[i] = MyCreateInstance(parameters[i].ParameterType, asm);
			}
			
			return ConstrMin.Invoke(constructorParam);
			//return Activator.CreateInstance(type, constructorParam);
		}
		
		static int TotalCount = 0;
		static void SetTotalPogressData(int total)
		{
			TotalCount = total;
		}

		static void ShowPogress(int CurrentCount)
		{
			double percent = ((double)CurrentCount/(double)TotalCount)*100;
			Console.Write("\r{0} of {1} {2}%", CurrentCount, TotalCount, (int)(percent));
		}
		
		public static void Testing(Assembly asm)
		{
			
			//Type type = asm.ManifestModule.ResolveType(0x020002F5);
			//object em_instance = GetGenericInstance(type, asm);
			//Type type = asm.ManifestModule.ResolveType(0x02000006);  // 0x02000006  08
			//object em_instance = GetGenericInstance(type, asm);
			
		}
		
		static Dictionary<Type, object> instances;
		static List<GCHandle> handles;
		public static void DecryptMethods(DecryptionMethods dec, Assembly asm, ModuleDefMD module)
		{
			handles = new List<GCHandle>();
			instances = new Dictionary<Type, object>();
			Module manifestModule = asm.ManifestModule;
			int Total = VMDecryptor.allEncMethods.Count;
			if (Total==0) return;
			int CurrentCount = 0;
			
			Console.WriteLine("Total VM methods = "+Total);
			SetTotalPogressData(Total);
			ShowPogress(0);

			int failedCount = 0;
			
			foreach (EncryptedMethodDetails encryptedMethodDetails in VMDecryptor.allEncMethods)
			{
				/*Console.Ge
				Console.SetCursorPosition(8, 0);
				Console.Write("{0}%", (CurrentCount/Total)*100);
				 */
				//ProgressBar(CurrentCount, Total);
				CurrentCount++;
				ShowPogress(CurrentCount);
				
				try
				{
					int encryptedValue = encryptedMethodDetails.encryptedValue;
					
					MethodBase methodBase = manifestModule.ResolveMethod(dec.thirdMethod.MDToken.ToInt32());
					
					object instance = null;

					//if (!methodBase.IsStatic)
					//{
						if (instances.ContainsKey(methodBase.DeclaringType))
						{
							instance = instances[methodBase.DeclaringType];
						}
						else
						{
							instance = Activator.CreateInstance(methodBase.DeclaringType);

							//Activator.CreateInstance(methodBase_r.DeclaringType);
							//if (!instances.ContainsKey(methodBase_r.DeclaringType))
							instances.Add(methodBase.DeclaringType, instance);
						}
					//}
					//object instance = Activator.CreateInstance(methodBase.DeclaringType);
					//continue;
					
					
					/*}
							catch(Exception exc)
							{

								Console.WriteLine("Exc "+methodBase_r.DeclaringType.MetadataToken.ToString("X8"));
								Console.WriteLine(exc.ToString());
							}*/
					
					//object instance = Activator.CreateInstance(methodBase.DeclaringType);
					
					object obj2 = methodBase.Invoke(instance, new object[]
					                                {
					                                	encryptedValue
					                                });
					
					/*
	thirdMethod:
	private Interface0 method_1(int int_0, MethodBase methodBase_0, object object_0)
	{
		Class29 class = this.class13_0.method_0(int_0);
		class.methodBase_0 = methodBase_0;
		Interface0 result;
		try
		{
			Interface0 interface = Class53.smethod_0(this.class32_0, class, methodBase_0);
			
	// Token: 0x060001BA RID: 442
	// RVA: 0x0000C300 File Offset: 0x0000A500
	internal static Interface0 smethod_0(Class32 class32_0, Class29 class29_0, MethodBase methodBase_0)
	{
		Class47 class = new Class47(class32_0.module_0, methodBase_0);
		DynamicMethod dynamicMethod = class.method_0(class29_0);
					 */
					
					Type type = manifestModule.ResolveType(dec.initalField.FieldType.ToTypeDefOrRef().MDToken.ToInt32());
					object obj3 = Activator.CreateInstance(type);
					FieldInfo fieldInfo = obj3.GetType().GetFields()[0];
					BindingFlags bindingAttr = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
					FieldInfo field = obj3.GetType().GetField(fieldInfo.Name, bindingAttr);
					object value = field.GetValue(obj3);
					MethodBase methodBase2 = manifestModule.ResolveMethod(dec.fifthMethod.MDToken.ToInt32());
					TypeDef typeDef = dec.initalType as TypeDef;
					MethodDef methodDef = dec.fifthMethod.DeclaringType.FindConstructors().ToArray<MethodDef>()[0];
					ConstructorInfo constructorInfo = (ConstructorInfo)manifestModule.ResolveMethod(methodDef.MDToken.ToInt32());

					object obj4 = constructorInfo.Invoke(new object[]
					                                     {
					                                     	value, null
					                                     });// added a null value - MethodBase
					
					object obj5 = methodBase2.Invoke(obj4, new object[]
					                                 {
					                                 	obj2
					                                 });
					

					
					SuperDynamicReader superDynamicReader = new SuperDynamicReader(module, obj5);
					if (superDynamicReader.codeSize<=0)
					{
						throw new Exception();
						//Console.WriteLine(encryptedMethodDetails.method.FullName + " Code size=0 on "+encryptedMethodDetails.method.MDToken.ToUInt32().ToString("X8"));
					}
					superDynamicReader.Read();
					superDynamicReader.RestoreMethod(encryptedMethodDetails.method);
				}
				catch(Exception exc)
				{

					MethodBase methodBase_r = manifestModule.ResolveMethod(encryptedMethodDetails.method.MDToken.ToInt32());
					
					object em_instance = null;
					if (!methodBase_r.IsStatic)
					{
						if (!methodBase_r.DeclaringType.IsAbstract)
						{
							if (methodBase_r.DeclaringType.ContainsGenericParameters)
							{

								if (instances.ContainsKey(methodBase_r.DeclaringType))
								{
									em_instance = instances[methodBase_r.DeclaringType];
								}
								else
								{
									em_instance = GetGenericInstance(methodBase_r.DeclaringType, asm);
									//if (!instances.ContainsKey(methodBase_r.DeclaringType))
									instances.Add(methodBase_r.DeclaringType, em_instance);
								}

								int OldMToken = methodBase_r.MetadataToken;
								if (methodBase_r.IsConstructor || methodBase_r is ConstructorInfo)
									methodBase_r = GetConstructor(em_instance.GetType(), methodBase_r);
								else
									methodBase_r = GetMethod(em_instance.GetType(), methodBase_r);
								if (methodBase_r.MetadataToken != OldMToken)
								{
									Console.WriteLine("Failed for");
									Console.WriteLine(OldMToken.ToString("X8") + methodBase_r.ToString());

								}

							}
							else
							{
								//try
								//{

								//if ((int)(methodBase_r.DeclaringType.MetadataToken)!=0x02000009)
								if (instances.ContainsKey(methodBase_r.DeclaringType))
								{
									em_instance = instances[methodBase_r.DeclaringType];
								}
								else
								{
									em_instance = MyCreateInstance(methodBase_r.DeclaringType, asm);

									//Activator.CreateInstance(methodBase_r.DeclaringType);
									//if (!instances.ContainsKey(methodBase_r.DeclaringType))
									instances.Add(methodBase_r.DeclaringType, em_instance);
								}


								/*}
								catch(Exception exc)
								{

									Console.WriteLine("Exc "+methodBase_r.DeclaringType.MetadataToken.ToString("X8"));
									Console.WriteLine(exc.ToString());
								}*/
							}
						}
						else
							em_instance = methodBase_r.DeclaringType.TypeHandle;
					}
					
					Type ftype = manifestModule.ResolveType(dec.firstField.FieldType.ToTypeDefOrRef().MDToken.ToInt32());
					BindingFlags bindingAttr1 = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
					FieldInfo rfield = ftype.GetField(dec.firstField.Name, bindingAttr1);
					object fvalue = rfield.GetValue(null);

					MethodBase firstmethodBase = manifestModule.ResolveMethod(dec.initalmethod.MDToken.ToInt32());

					object interfaceobj = null;

					MethodBase oldmethodBase_r = methodBase_r;

                    try
					{
						if (!ContainsString(encryptedMethodDetails.method,"MethodBase::get_MethodHandle()")&& !ContainsString(encryptedMethodDetails.method, "MethodBase::GetCurrentMethod()"))
                            methodBase_r = null;
                        
                        if (encryptedMethodDetails.method.IsStatic)
                        {
                            em_instance = null;
                        }
						else if (em_instance==null)
                        {
							Console.WriteLine("Instance can't be null!");
							failedCount++;
							continue;

						}

                        /*if (encryptedMethodDetails.method.MDToken.ToInt32() == 0x060001F0)
						{
							//Console.ReadKey();
							//if (methodBase_r == null)
							//	Console.WriteLine("WTF?");
							//   Console.ReadKey();
							//if (methodBase_r != null)
							//Console.WriteLine(methodBase_r.ToString());


						}
						*/
						interfaceobj = firstmethodBase.Invoke(fvalue, new object[]
															  {
																  encryptedMethodDetails.encryptedValue, methodBase_r, em_instance
															  });

						/*if (encryptedMethodDetails.method.MDToken.ToInt32() == 0x060001F0)
						{
							Console.WriteLine("great!");
                        }
						*/
					}
					catch (Exception exc1)
					{

						/*
MethodBase get_AdvancedOptions = runtimeAssembly.ManifestModule.ResolveMethod(0x060001F0);
try
{
    bool IsAdanced = (bool)get_AdvancedOptions.Invoke(null, new object[0]);
}
catch(Exception exc3)
{
    Console.WriteLine(exc3.ToString());
}
						*/

						failedCount++;

                        Console.WriteLine(((uint)oldmethodBase_r.MetadataToken).ToString("X8") + " " + oldmethodBase_r.ToString());
						Console.WriteLine(exc1.ToString());
                        //Console.ReadKey();

                    }
					
					if (interfaceobj!=null)
					{
						
						Type interf_type = interfaceobj.GetType();
						ConstructorInfo[] constructors = interf_type.GetConstructors();
						bool isDirectDelegate = false;

						if (constructors[0].GetParameters().ElementAt(0).ParameterType==typeof(Delegate))
							isDirectDelegate = true;
						
						FieldInfo[] fields = interf_type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
						object obj_value = null;
						string str_value = "";
						Delegate directDelegate = null;
						for (int i=0;i<fields.Length;i++)
						{
							if (isDirectDelegate&&fields[i].FieldType.Equals(typeof(Delegate)))
							{
								directDelegate = (Delegate)fields[i].GetValue(interfaceobj);
								break;
							}
							
							if (!isDirectDelegate)
							{
								if (fields[i].FieldType.Equals(typeof(object)))
									obj_value = (object)fields[i].GetValue(interfaceobj);

								if (fields[i].FieldType == typeof(string))
									str_value = (string)fields[i].GetValue(interfaceobj);
							}
							
						}

						/*
                        if (oldmethodBase_r.MetadataToken==0x06000067)
						{
                            if (interfaceobj != null)
                            {

								Console.WriteLine("Kool!");
                                
                            }
                        }
						*/

                        if (obj_value!=null&&str_value!="")
						{
							directDelegate = (Delegate)obj_value.GetType().InvokeMember(str_value, BindingFlags.InvokeMethod, null, obj_value, new object[]
							                                                            {
							                                                            	null
							                                                            });
						}

						if (directDelegate!=null)
						{

							//try
							//{
							System.Reflection.Emit.DynamicMethod dynamic_m = null;

                            Type RTDynamicMethod = typeof(System.Reflection.Emit.DynamicMethod).GetNestedType("RTDynamicMethod",BindingFlags.NonPublic);
							if (RTDynamicMethod != null)
							{
								FieldInfo owner = RTDynamicMethod.
									GetField("m_owner", BindingFlags.Instance | BindingFlags.NonPublic);
								dynamic_m = (System.Reflection.Emit.DynamicMethod)owner.GetValue(directDelegate.Method);
							}
							else
							{
								dynamic_m = (System.Reflection.Emit.DynamicMethod)directDelegate.Method;
                            }

								if (dynamic_m!=null)
								{
                                    SuperDynamicReader superDynamicReader = new SuperDynamicReader(module, dynamic_m);
									if (superDynamicReader.codeSize>0)
									{
                                        superDynamicReader.Read();
										superDynamicReader.RestoreMethod(encryptedMethodDetails.method);
									}
									else
									{
										Console.WriteLine(encryptedMethodDetails.method.FullName + " Code size=0 on "+encryptedMethodDetails.method.MDToken.ToUInt32().ToString("X8"));
									}
                                
                                }


							
						}
						
					}

				}
				//continue;
				

				//if (encryptedMethodDetails.method.MDToken.ToInt32()!=0x06000150)
				//continue;
				//try  // Second decryption way:
				//	{
				//Console.WriteLine(encryptedMethodDetails.method.FullName + " decrypt "+encryptedMethodDetails.method.MDToken.ToUInt32().ToString("X8"));
				
				//	}
				
				//	catch
				//	{
				
				//

//				}
				//}
			}

            Console.WriteLine("\r\nFailed count = "+ failedCount.ToString());
            Console.WriteLine();
			
			instances = new Dictionary<Type, object>();
			if (handles!=null&&handles.Count>0)
			{// free memory
				foreach (GCHandle handle in handles)
					if (handle.IsAllocated)
						handle.Free();
			}
			
		}
		public static DecryptionMethods setUpDecryptionRoutine(MethodDef methods)
		{
			DecryptionMethods decryptionMethods = new DecryptionMethods();
			MethodDef methodDef = methods.Body.Instructions[methods.Body.Instructions.Count - 3].Operand as MethodDef;
			for (int i = 0; i < methodDef.Body.Instructions.Count; i++)
			{
				if (methodDef.Body.Instructions[i].OpCode == dnlib.DotNet.Emit.OpCodes.Callvirt)
				{
					decryptionMethods.initalmethod = (methodDef.Body.Instructions[i].Operand as MethodDef);
					bool isField = (((i-4)>=0)&&methodDef.Body.Instructions[i-4].OpCode == dnlib.DotNet.Emit.OpCodes.Ldsfld);
					if (isField)
						decryptionMethods.firstField = methodDef.Body.Instructions[i-4].Operand as FieldDef;
					break;
				}
			}
			for (int j = 0; j < decryptionMethods.initalmethod.Body.Instructions.Count; j++)
			{
				if (decryptionMethods.initalmethod.Body.Instructions[j].OpCode == dnlib.DotNet.Emit.OpCodes.Call && decryptionMethods.initalmethod.Body.Instructions[j].Operand is MethodDef)
				{
					MethodDef methodDef2 = decryptionMethods.initalmethod.Body.Instructions[j].Operand as MethodDef;
					if ((methodDef2.Parameters.Count == 2||methodDef2.Parameters.Count == 4) && methodDef2.HasReturnType)
					{
						decryptionMethods.secondMethod = methodDef2;
					}
					break;
				}
			}
			for (int k = 0; k < decryptionMethods.secondMethod.Body.Instructions.Count; k++)
			{
				if (decryptionMethods.secondMethod.Body.Instructions[k].OpCode == dnlib.DotNet.Emit.OpCodes.Callvirt && decryptionMethods.secondMethod.Body.Instructions[k].Operand is MethodDef)
				{
					MethodDef methodDef3 = decryptionMethods.secondMethod.Body.Instructions[k].Operand as MethodDef;
					if ((methodDef3.Parameters.Count == 2) && methodDef3.HasReturnType)
					{
						decryptionMethods.thirdMethod = methodDef3;
					}
				}
				bool flag11 = decryptionMethods.secondMethod.Body.Instructions[k].OpCode == dnlib.DotNet.Emit.OpCodes.Ldfld && decryptionMethods.secondMethod.Body.Instructions[k].Operand is FieldDef;
				bool flag2_11 = (decryptionMethods.secondMethod.Body.Instructions[k + 3].OpCode == dnlib.DotNet.Emit.OpCodes.Call ||decryptionMethods.secondMethod.Body.Instructions[k + 3].OpCode == dnlib.DotNet.Emit.OpCodes.Callvirt) && decryptionMethods.secondMethod.Body.Instructions[k + 3].Operand is MethodDef;
				
				if (flag11&&flag2_11)
				{
					MethodDef methodDef4 = decryptionMethods.secondMethod.Body.Instructions[k + 3].Operand as MethodDef;
					if ((methodDef4.Parameters.Count == 3) && methodDef4.HasReturnType)
					{
						decryptionMethods.fourthMethod = methodDef4;
						decryptionMethods.initalField = (FieldDef)decryptionMethods.secondMethod.Body.Instructions[k].Operand;
						break;
					}

				}
			}
			decryptionMethods.initalType = (methods.Module.ResolveToken(33554456) as ITypeDefOrRef);
			for (int l = 0; l < decryptionMethods.fourthMethod.Body.Instructions.Count; l++)
			{
				if (decryptionMethods.fourthMethod.Body.Instructions[l].IsLdloc() && decryptionMethods.fourthMethod.Body.Instructions[l + 1].IsLdarg() && decryptionMethods.fourthMethod.Body.Instructions[l + 2].OpCode == dnlib.DotNet.Emit.OpCodes.Callvirt && decryptionMethods.fourthMethod.Body.Instructions[l + 3].IsStloc())
				{
					MethodDef methodDef5 = decryptionMethods.fourthMethod.Body.Instructions[l + 2].Operand as MethodDef;
					if ((methodDef5.Parameters.Count == 2||methodDef5.Parameters.Count == 4) && methodDef5.HasReturnType)
					{
						decryptionMethods.fifthMethod = methodDef5;
					}
					break;
				}
			}
			return decryptionMethods;
		}
		public static ITypeDefOrRef finder(LocalList locals)
		{
			foreach (Local local in locals)
			{
				if (local.Type.ElementType == ElementType.Class)
				{
					return local.Type.ToTypeDefOrRef();
				}
			}
			return null;
		}

		public static bool ContainsString(MethodDef method, string ToBeThere)
		{
			if (!method.HasBody)
				return false;

			for (int i = 0; i < method.Body.Instructions.Count; i++)
			{
				if (method.Body.Instructions[i].OpCode == dnlib.DotNet.Emit.OpCodes.Call || method.Body.Instructions[i].OpCode == dnlib.DotNet.Emit.OpCodes.Callvirt)
				{
					if (method.Body.Instructions[i].Operand!=null)
					{
						string str = method.Body.Instructions[i].Operand.ToString();
						if (str.Contains(ToBeThere))
							return true;

                    }

                }
			}
			return false;
		}
		public static void FindEncryptedMethods(ModuleDefMD module)
		{
			foreach (TypeDef typeDef in module.GetTypes())
			{
				foreach (MethodDef methodDef in typeDef.Methods)
				{

                    if (methodDef.HasBody)
					{
						/*
						if (methodDef.Body.Instructions.Count >= 7+8)
						{
                            if (methodDef.Body.Instructions[0].IsLdcI4()&&
								methodDef.Body.Instructions[1].OpCode== dnlib.DotNet.Emit.OpCodes.Newarr&&
                                methodDef.Body.Instructions[2].OpCode == dnlib.DotNet.Emit.OpCodes.Dup&&
                                methodDef.Body.Instructions[3].IsLdcI4())
							{
								if (methodDef.Body.Instructions[1].Operand != null &&
									methodDef.Body.Instructions[1].Operand.ToString().Contains("System.Object"))
								{
									int stlocIndex = 3;
									while (stlocIndex < methodDef.Body.Instructions.Count
										&& !methodDef.Body.Instructions[stlocIndex].IsStloc())
									{
										stlocIndex++;
                                    }

									if (stlocIndex < methodDef.Body.Instructions.Count
										&& methodDef.Body.Instructions[stlocIndex].IsStloc())
									{
										bool Flag1 = methodDef.Body.Instructions[0].IsLdcI4();
                                        Console.Write(Flag1);

                                        Console.WriteLine(methodDef.MDToken.ToInt32().ToString("X8"));
									}
								}

                            }

                        }*/

						bool flag5 = methodDef.Body.Instructions.Count > 70;
						bool flag6 = !flag5;
						if (flag6)
						{
							bool flag7 = !(methodDef.Body.Instructions.Count>=7&&(methodDef.Body.Instructions[methodDef.Body.Instructions.Count - 7].IsLdcI4()||methodDef.Body.Instructions[0].IsLdcI4()));
							bool flag8 = !flag7;
							if (flag8)
							{
								bool flag9 = methodDef.Body.Instructions.Count < 3;
								bool flag10 = !flag9;
								if (flag10)
								{
									if (methodDef.Body.Instructions[methodDef.Body.Instructions.Count - 3].OpCode == dnlib.DotNet.Emit.OpCodes.Call)
									{

										if (VMDecryptor.checkMethod(methodDef))
										{
											if (methodDef.Body.Instructions.Count>=7)
											{

												if (methodDef.Body.Instructions[methodDef.Body.Instructions.Count - 3].OpCode == dnlib.DotNet.Emit.OpCodes.Call&&
												    (methodDef.Body.Instructions[methodDef.Body.Instructions.Count - 3].Operand is MethodDef)&&
												    (methodDef.Body.Instructions[methodDef.Body.Instructions.Count - 3].Operand as MethodDef).Parameters.Count==4)
												{


													if (methodDef.Body.Instructions[methodDef.Body.Instructions.Count - 6].OpCode == dnlib.DotNet.Emit.OpCodes.Ldnull ||
													 methodDef.Body.Instructions[methodDef.Body.Instructions.Count - 5].OpCode == dnlib.DotNet.Emit.OpCodes.Ldnull ||
													 methodDef.Body.Instructions[methodDef.Body.Instructions.Count - 5].OpCode == dnlib.DotNet.Emit.OpCodes.Ldarg_0 ||
													 methodDef.Body.Instructions[methodDef.Body.Instructions.Count - 4].OpCode == dnlib.DotNet.Emit.OpCodes.Ldnull)
													{
														/*if (methodDef.Body.Instructions[methodDef.Body.Instructions.Count - 5].OpCode == dnlib.DotNet.Emit.OpCodes.Ldarg_0)
														{
															Console.WriteLine(methodDef.MDToken.ToInt32().ToString("X8"));
														}
														*/

                                                        int ldcI4Value = -1;

														for (int i = methodDef.Body.Instructions.Count - 7; i >= 0; i--)
														{
															if (methodDef.Body.Instructions[i].IsLdcI4())
															{
																ldcI4Value = methodDef.Body.Instructions[i].GetLdcI4Value();
																break;
															}
														}

														if (ldcI4Value != -1)
														{
															EncryptedMethodDetails item = new EncryptedMethodDetails(methodDef, ldcI4Value);
															VMDecryptor.allEncMethods.Add(item);

															/*if (methodDef.MDToken.ToInt32() == 0x060001F0)
															{
																Console.WriteLine("Kool!");
															}
															*/
														}
													}
													
												}
											}
											

										}
									}
								}
							}
						}
					}
				}
			}
		}
		private static bool checkMethod(MethodDef method)
		{
			for (int i = 0; i < method.Body.Instructions.Count; i++)
			{
				if (method.Body.Instructions[i].OpCode == dnlib.DotNet.Emit.OpCodes.Call && method.Body.Instructions[i].Operand is MethodDef)
				{
					MethodDef methodDef = (MethodDef)method.Body.Instructions[i].Operand;
					bool result;
					if (methodDef.ReturnType.FullName.Contains("Int32"))
					{
						result = false;
					}
					else
					{  // was 40 41 42  but it is also 44
						
						if (!methodDef.HasBody)
							continue;
						
						bool hasRightCount = methodDef.Body.Instructions.Count == 41 || methodDef.Body.Instructions.Count == 40|| methodDef.Body.Instructions.Count == 42|| methodDef.Body.Instructions.Count == 44;
						if (!hasRightCount)
							continue;
						
						result = true;
					}
					return result;
				}

			}
			return false;
		}
		public static List<EncryptedMethodDetails> allEncMethods = new List<EncryptedMethodDetails>();
	}
}
