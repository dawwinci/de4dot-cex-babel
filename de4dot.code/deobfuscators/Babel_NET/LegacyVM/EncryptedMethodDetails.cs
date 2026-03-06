using System;
using dnlib.DotNet;

namespace de4dot.code.deobfuscators.Babel_NET.LegacyVM
{
	public class EncryptedMethodDetails
	{
		public EncryptedMethodDetails(MethodDef origMethod, int EncryptedValue)
		{
			this.method = origMethod;
			this.encryptedValue = EncryptedValue;
		}
		public int encryptedValue;
		public MethodDef method;
	}
}
