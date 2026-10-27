// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using JxlSharp.Tools;

namespace JxlSharp.Cli.JxlTran
{
    /// <summary>
    /// Entry point for the <c>jxltran</c> command-line container tool.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Main entry point for <c>jxltran</c>.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Process exit code.</returns>
        public static int Main(string[] args)
        {
            return JxlTranTool.Run(args);
        }
    }
}
