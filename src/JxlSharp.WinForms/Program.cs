// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using System.IO;
using System.Windows.Forms;
using JxlSharp.Tools;

namespace JxlSharp.WinForms
{
    /// <summary>
    /// Application entry point for the <c>JxlSharp.WinForms</c> desktop application.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Main entry point for the application.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Process exit code.</returns>
        [STAThread]
        public static int Main(string[] args)
        {
            if (args is not null && args.Length > 0 && string.Equals(args[0], "--cli", StringComparison.OrdinalIgnoreCase))
            {
                return DispatchCliCommand(args, Console.Out, Console.Error);
            }

            ApplicationConfiguration.Initialize();

            using MainForm form = CreateMainForm(args);

            Application.Run(form);

            return 0;
        }

        /// <summary>
        /// Creates and initializes a <see cref="MainForm"/> instance, optionally loading an initial file path from <paramref name="args"/>.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Initialized <see cref="MainForm"/>.</returns>
        public static MainForm CreateMainForm(string[]? args)
        {
            MainForm form = new MainForm();

            if (args is not null && args.Length > 0 && File.Exists(args[0]))
            {
                form.LoadFromFilePath(args[0]);
            }

            return form;
        }

        /// <summary>
        /// Dispatches a <c>--cli &lt;tool&gt; [args...]</c> command line to <c>cjxl</c>, <c>djxl</c>, <c>jxlinfo</c>, or <c>jxltran</c>.
        /// </summary>
        /// <param name="args">Full argument array starting with <c>--cli</c>.</param>
        /// <param name="stdout">Optional standard output writer.</param>
        /// <param name="stderr">Optional standard error writer.</param>
        /// <returns>CLI tool exit code.</returns>
        public static int DispatchCliCommand(string[] args, TextWriter? stdout = null, TextWriter? stderr = null)
        {
            TextWriter errWriter = stderr ?? Console.Error;

            if (args is null || args.Length < 2)
            {
                errWriter.WriteLine("Usage: JxlSharp.WinForms --cli <cjxl|djxl|jxlinfo|jxltran> [options...]");

                return 1;
            }

            string tool       = args[1].ToLowerInvariant();
            string[] toolArgs = new string[args.Length - 2];

            if (toolArgs.Length > 0)
            {
                Array.Copy(args, 2, toolArgs, 0, toolArgs.Length);
            }

            return tool switch
            {
                "cjxl"    => CjxlTool.Run(toolArgs, stdout, stderr),
                "djxl"    => DjxlTool.Run(toolArgs, stdout, stderr),
                "jxlinfo" => JxlInfoTool.Run(toolArgs, stdout, stderr),
                "jxltran" => JxlTranTool.Run(toolArgs, stdout, stderr),
                _         => WriteUnknownToolError(tool, errWriter),
            };
        }

        private static int WriteUnknownToolError(string tool, TextWriter errWriter)
        {
            errWriter.WriteLine($"Unknown CLI tool: {tool}");

            return 1;
        }
    }
}
