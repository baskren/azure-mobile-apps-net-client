using System;

namespace Microsoft.WindowsAzure.MobileServices
{
    public static class DebugExtensions
    {
        public static string CallerMemberName([System.Runtime.CompilerServices.CallerMemberName] string callerName = null)
            => callerName;

        public static string CallerString([System.Runtime.CompilerServices.CallerMemberName] string callerName = null, [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0)
            => callerName + ":" + lineNumber;
    }
}
