// ******************************************************************************
//  © 2018 Sebastiaan Dammann | damsteen.nl
// 
//  File:           : StringPathExtensions.cs
//  Project         : SystemWebpack
// ******************************************************************************

namespace SystemWebpack.Core.Abstractions {
    using System;

    internal static class StringPathExtensions {
        public static bool StartsWithSegments(this string path, string subPath) {
            return path.StartsWith(subPath, StringComparison.OrdinalIgnoreCase);
        }
    }
}