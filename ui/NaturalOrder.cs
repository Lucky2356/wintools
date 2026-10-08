using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Wintools
{
    // Sorts names the way Explorer does: "App 2" before "App 10", case and accents ignored.
    internal sealed class NaturalOrder : IComparer<string>
    {
        internal static readonly NaturalOrder Instance = new NaturalOrder();

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string first, string second);

        public int Compare(string first, string second)
        {
            return StrCmpLogicalW(first ?? "", second ?? "");
        }
    }
}
