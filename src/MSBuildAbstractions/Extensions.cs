﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSBuildAbstractions
{
    public static class Extensions
    {
        public static bool ContainsIgnoreCase(this string target, string substring)
        {
            return target.IndexOf(substring, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
