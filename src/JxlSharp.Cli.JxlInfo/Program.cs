// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using JxlSharp.Tools;

namespace JxlSharp.Cli.JxlInfo
{
    /// <summary>
    /// Entry point for the <c>jxlinfo</c> command-line inspection tool.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Main entry point for <c>jxlinfo</c>.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Process exit code.</returns>
        public static int Main(string[] args)
        {
            return JxlInfoTool.Run(args);
        }
    }
}
