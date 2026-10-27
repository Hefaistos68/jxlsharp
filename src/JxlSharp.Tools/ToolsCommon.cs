// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using JxlSharp.Base;
using JxlSharp.Extras;

namespace JxlSharp.Tools
{
    /// <summary>
    /// Delegate for parsing a command-line option argument into a value of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Target option value type.</typeparam>
    /// <param name="arg">Argument string.</param>
    /// <param name="value">Parsed output value.</param>
    /// <returns><c>true</c> if parsing succeeded; otherwise <c>false</c>.</returns>
    public delegate bool OptionValueParser<T>(string arg, out T value);

    /// <summary>
    /// Delegate for parsing a command-line flag that takes no argument.
    /// </summary>
    /// <typeparam name="T">Target flag state type.</typeparam>
    /// <param name="value">Output state value.</param>
    /// <returns><c>true</c> if flag handling succeeded; otherwise <c>false</c>.</returns>
    public delegate bool OptionFlagParser<T>(out T value);

    /// <summary>
    /// Abstract base class for command-line option definitions.
    /// Ported from <c>jpegxl::tools::CommandLineParser::CmdOptionInterface</c> in <c>tools/cmdline.h</c>.
    /// </summary>
    public abstract class CmdOptionInterface
    {
        /// <summary>
        /// Gets a value indicating whether the option was matched during parsing.
        /// </summary>
        public abstract bool Matched { get; }

        /// <summary>
        /// Returns a string with the option name or available flags.
        /// </summary>
        /// <returns>Formatted option flags string.</returns>
        public abstract string HelpFlags();

        /// <summary>
        /// Returns the help string if any, or <c>null</c> if no help string.
        /// </summary>
        /// <returns>Help text or <c>null</c>.</returns>
        public abstract string? HelpText();

        /// <summary>
        /// Returns the verbosity level at which this option is displayed.
        /// </summary>
        /// <returns>Verbosity level.</returns>
        public abstract int VerbosityLevel();

        /// <summary>
        /// Returns whether the option was passed on the command line.
        /// </summary>
        /// <returns><c>true</c> if matched; otherwise <c>false</c>.</returns>
        public bool IsMatched() => Matched;

        /// <summary>
        /// Returns whether this option matches the passed command-line argument.
        /// </summary>
        /// <param name="arg">Current command-line token.</param>
        /// <param name="parseOptions">Whether flag matching is currently enabled.</param>
        /// <returns><c>true</c> if this option matches <paramref name="arg"/>.</returns>
        public abstract bool Match(string arg, bool parseOptions);

        /// <summary>
        /// Parses the option starting at index <paramref name="i"/> in <paramref name="argv"/>.
        /// </summary>
        /// <param name="argv">Command-line arguments array.</param>
        /// <param name="i">Current argument index, advanced on success.</param>
        /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
        public abstract bool Parse(string[] argv, ref int i);

        /// <summary>
        /// Returns whether the option is positional.
        /// </summary>
        /// <returns><c>true</c> if positional; otherwise <c>false</c>.</returns>
        public abstract bool Positional();

        /// <summary>
        /// Returns whether the positional option is displayed as required in the help output.
        /// </summary>
        /// <returns><c>true</c> if required; otherwise <c>false</c>.</returns>
        public abstract bool Required();

        /// <summary>
        /// Returns whether this entry is help-text-only rather than an actual option.
        /// </summary>
        /// <returns><c>true</c> if help text only; otherwise <c>false</c>.</returns>
        public abstract bool HelpOnly();
    }

    /// <summary>
    /// Command-line argument and flag parser for JPEG XL tools.
    /// Ported from <c>jpegxl::tools::CommandLineParser</c> in <c>tools/cmdline.h</c> and <c>tools/cmdline.cc</c>.
    /// </summary>
    public sealed class CommandLineParser
    {
        private readonly List<CmdOptionInterface> _options = new List<CmdOptionInterface>();
        private string? _programName;
        private bool _help;

        /// <summary>
        /// Gets or sets the verbosity level accumulated from <c>-v</c> / <c>--verbose</c> flags.
        /// </summary>
        public int Verbosity { get; set; }

        /// <summary>
        /// Returns whether a <c>-h</c> or <c>--help</c> flag was specified.
        /// </summary>
        /// <returns><c>true</c> if help was requested; otherwise <c>false</c>.</returns>
        public bool HelpFlagPassed() => _help;

        /// <summary>
        /// Adds a section of help text at the given verbosity level.
        /// </summary>
        /// <param name="helpText">Help text to display.</param>
        /// <param name="verbosityLevel">Minimum verbosity required to show this text.</param>
        public void AddHelpText(string helpText, int verbosityLevel = 0)
        {
            _options.Add(new CmdHelpText(helpText, verbosityLevel));
        }

        /// <summary>
        /// Adds a positional argument option using a setter callback.
        /// </summary>
        /// <param name="name">Display name of the positional argument.</param>
        /// <param name="required">Whether the positional argument is displayed as required in help.</param>
        /// <param name="helpText">Help description.</param>
        /// <param name="setStorage">Callback receiving the matched argument string.</param>
        /// <param name="verbosityLevel">Minimum verbosity level to display this option.</param>
        /// <returns>Option identifier index.</returns>
        public int AddPositionalOption(
            string name,
            bool required,
            string helpText,
            Action<string> setStorage,
            int verbosityLevel = 0)
        {
            _options.Add(new CmdOptionPositional(name, helpText, setStorage, verbosityLevel, required));

            return _options.Count - 1;
        }

        /// <summary>
        /// Adds a positional argument option backed by a <see cref="StrongBox{T}"/>.
        /// </summary>
        /// <param name="name">Display name of the positional argument.</param>
        /// <param name="required">Whether the positional argument is displayed as required in help.</param>
        /// <param name="helpText">Help description.</param>
        /// <param name="storage">Box receiving the matched argument string.</param>
        /// <param name="verbosityLevel">Minimum verbosity level to display this option.</param>
        /// <returns>Option identifier index.</returns>
        public int AddPositionalOption(
            string name,
            bool required,
            string helpText,
            StrongBox<string?> storage,
            int verbosityLevel = 0)
        {
            return AddPositionalOption(name, required, helpText, val => storage.Value = val, verbosityLevel);
        }

        /// <summary>
        /// Adds an option that takes a value of type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Option value type.</typeparam>
        /// <param name="shortName">Short option character (or <c>'\0'</c> for none).</param>
        /// <param name="longName">Long option name (or <c>null</c> for none).</param>
        /// <param name="metavar">Placeholder name for the value in help text.</param>
        /// <param name="helpText">Help description.</param>
        /// <param name="setStorage">Setter callback invoked when the value is parsed.</param>
        /// <param name="parser">Parser function converting the string argument to <typeparamref name="T"/>.</param>
        /// <param name="verbosityLevel">Minimum verbosity level to display this option.</param>
        /// <returns>Option identifier index.</returns>
        public int AddOptionValue<T>(
            char shortName,
            string? longName,
            string metavar,
            string? helpText,
            Action<T> setStorage,
            OptionValueParser<T> parser,
            int verbosityLevel = 0)
        {
            Func<string, bool> parseHandler = arg =>
            {
                if (!parser(arg, out T parsed))
                {
                    return false;
                }

                setStorage(parsed);

                return true;
            };

            _options.Add(new CmdOptionFlag(shortName, longName, metavar, helpText, parseHandler, null, verbosityLevel));

            return _options.Count - 1;
        }

        /// <summary>
        /// Adds an option that mutates an existing target instance <paramref name="storage"/>.
        /// </summary>
        /// <typeparam name="T">Target storage type.</typeparam>
        /// <param name="shortName">Short option character (or <c>'\0'</c> for none).</param>
        /// <param name="longName">Long option name (or <c>null</c> for none).</param>
        /// <param name="metavar">Placeholder name for the value in help text.</param>
        /// <param name="helpText">Help description.</param>
        /// <param name="storage">Target object passed to <paramref name="parser"/>.</param>
        /// <param name="parser">Parser function accepting the raw argument and <paramref name="storage"/>.</param>
        /// <param name="verbosityLevel">Minimum verbosity level to display this option.</param>
        /// <returns>Option identifier index.</returns>
        public int AddOptionValue<T>(
            char shortName,
            string? longName,
            string metavar,
            string? helpText,
            T storage,
            Func<string, T, bool> parser,
            int verbosityLevel = 0)
        {
            Func<string, bool> parseHandler = arg => parser(arg, storage);
            _options.Add(new CmdOptionFlag(shortName, longName, metavar, helpText, parseHandler, null, verbosityLevel));

            return _options.Count - 1;
        }

        /// <summary>
        /// Adds an option that takes a value using a direct parser callback.
        /// </summary>
        /// <typeparam name="T">Option value type.</typeparam>
        /// <param name="shortName">Short option character (or <c>'\0'</c> for none).</param>
        /// <param name="longName">Long option name (or <c>null</c> for none).</param>
        /// <param name="metavar">Placeholder name for the value in help text.</param>
        /// <param name="helpText">Help description.</param>
        /// <param name="parser">Parser callback receiving the string value.</param>
        /// <param name="verbosityLevel">Minimum verbosity level to display this option.</param>
        /// <returns>Option identifier index.</returns>
        public int AddOptionValue<T>(
            char shortName,
            string? longName,
            string metavar,
            string? helpText,
            Func<string, bool> parser,
            int verbosityLevel = 0)
        {
            _options.Add(new CmdOptionFlag(shortName, longName, metavar, helpText, parser, null, verbosityLevel));

            return _options.Count - 1;
        }

        /// <summary>
        /// Adds a flag without a value.
        /// </summary>
        /// <typeparam name="T">Flag value type.</typeparam>
        /// <param name="shortName">Short option character (or <c>'\0'</c> for none).</param>
        /// <param name="longName">Long option name (or <c>null</c> for none).</param>
        /// <param name="helpText">Help description.</param>
        /// <param name="setStorage">Setter callback invoked when the flag is matched.</param>
        /// <param name="parser">Flag handler producing the new <typeparamref name="T"/> value.</param>
        /// <param name="verbosityLevel">Minimum verbosity level to display this option.</param>
        /// <returns>Option identifier index.</returns>
        public int AddOptionFlag<T>(
            char shortName,
            string? longName,
            string? helpText,
            Action<T> setStorage,
            OptionFlagParser<T> parser,
            int verbosityLevel = 0)
        {
            Func<bool> flagHandler = () =>
            {
                if (!parser(out T parsed))
                {
                    return false;
                }

                setStorage(parsed);

                return true;
            };

            _options.Add(new CmdOptionFlag(shortName, longName, null, helpText, null, flagHandler, verbosityLevel));

            return _options.Count - 1;
        }

        /// <summary>
        /// Adds a flag without a value using a direct callback.
        /// </summary>
        /// <typeparam name="T">Flag value type.</typeparam>
        /// <param name="shortName">Short option character (or <c>'\0'</c> for none).</param>
        /// <param name="longName">Long option name (or <c>null</c> for none).</param>
        /// <param name="helpText">Help description.</param>
        /// <param name="parser">Callback invoked when the flag is matched.</param>
        /// <param name="verbosityLevel">Minimum verbosity level to display this option.</param>
        /// <returns>Option identifier index.</returns>
        public int AddOptionFlag<T>(
            char shortName,
            string? longName,
            string? helpText,
            Func<bool> parser,
            int verbosityLevel = 0)
        {
            _options.Add(new CmdOptionFlag(shortName, longName, null, helpText, null, parser, verbosityLevel));

            return _options.Count - 1;
        }

        /// <summary>
        /// Gets the <see cref="CmdOptionInterface"/> for the specified option ID.
        /// </summary>
        /// <param name="id">Option identifier returned by <c>Add*</c>.</param>
        /// <returns>The corresponding <see cref="CmdOptionInterface"/>.</returns>
        public CmdOptionInterface GetOption(int id)
        {
            return _options[id];
        }

        /// <summary>
        /// Prints the formatted help message to standard output.
        /// </summary>
        public void PrintHelp()
        {
            TextWriter output = Console.Out;
            output.Write("Usage: {0}", _programName ?? "command");

            WritePositionalUsage(output);
            output.Write(" [OPTIONS...]\n");

            bool showedAll = WriteOptionsHelp(output, out int maxVerbosity);
            output.Write("\n -h, --help\n    Prints this help message. ");

            if (showedAll)
            {
                output.Write("All options are shown above.\n");
            }
            else
            {
                output.Write("Add -v (up to a total of {0} times) to see more options.\n", maxVerbosity);
            }
        }

        /// <summary>
        /// Parses the command-line arguments array.
        /// Following <c>cmdline.cc</c>, <c>args[0]</c> is treated as the program name unless it starts with <c>'-'</c>.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns><c>true</c> if all arguments were parsed successfully; otherwise <c>false</c>.</returns>
        public bool Parse(string[] args)
        {
            if (args == null)
            {
                return false;
            }

            int i = 0;

            if (args.Length > 0 && (args[0].Length == 0 || args[0][0] != '-' || args[0] == "-"))
            {
                bool hasPositionalOnly = args.Length == 1 && _options.Exists(static o => o.Positional());
                _ = hasPositionalOnly;
                _programName = args[0];
                i            = 1;
            }

            bool parseOptions = true;

            while (i < args.Length)
            {
                if (!ParseSingleStep(args, ref i, ref parseOptions))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Conditionally prints a formatted message to standard error when <see cref="Verbosity"/> is at least <paramref name="minVerbosity"/>.
        /// </summary>
        /// <param name="minVerbosity">Minimum verbosity level required.</param>
        /// <param name="format">Composite or printf-style message string.</param>
        /// <param name="args">Optional format arguments.</param>
        public void VerbosePrintf(int minVerbosity, string format, params object[] args)
        {
            if (minVerbosity > Verbosity)
            {
                return;
            }

            string text = args != null && args.Length > 0
                ? string.Format(CultureInfo.InvariantCulture, format, args)
                : format;

            Console.Error.Write(text);
            Console.Error.Flush();
        }

        private void WritePositionalUsage(TextWriter output)
        {
            foreach (CmdOptionInterface option in _options)
            {
                if (!option.Positional() || option.VerbosityLevel() > Verbosity)
                {
                    continue;
                }

                string fmt = option.Required() ? " {0}" : " [{0}]";
                output.Write(fmt, option.HelpFlags());
            }
        }

        private bool WriteOptionsHelp(TextWriter output, out int maxVerbosity)
        {
            bool showedAll = true;
            maxVerbosity   = 0;

            foreach (CmdOptionInterface option in _options)
            {
                maxVerbosity = Math.Max(option.VerbosityLevel(), maxVerbosity);

                if (option.VerbosityLevel() > Verbosity)
                {
                    showedAll = false;
                    continue;
                }

                if (option.HelpOnly())
                {
                    output.Write("{0}\n", option.HelpText());
                    continue;
                }

                output.Write(" {0}\n", option.HelpFlags());
                string? helpText = option.HelpText();

                if (helpText != null)
                {
                    output.Write("    {0}\n", helpText);
                }
            }

            return showedAll;
        }

        private bool ParseSingleStep(string[] argv, ref int i, ref bool parseOptions)
        {
            string current = argv[i];

            if (current == "-h" || current == "--help")
            {
                _help = true;
                i++;

                return true;
            }

            if (current == "-v" || current == "--verbose")
            {
                Verbosity++;
            }

            if (current == "--")
            {
                parseOptions = false;
                i++;

                return true;
            }

            bool parseThisOption = current != "-";

            foreach (CmdOptionInterface option in _options)
            {
                if (option.Match(current, parseOptions && parseThisOption))
                {
                    if (!option.Parse(argv, ref i))
                    {
                        Console.Error.WriteLine($"Error parsing flag {current}");

                        return false;
                    }

                    return true;
                }
            }

            Console.Error.WriteLine($"Unknown argument: {current}");

            return false;
        }

        private sealed class CmdHelpText : CmdOptionInterface
        {
            private readonly string _helpText;
            private readonly int _verbosityLevel;

            public CmdHelpText(string helpText, int verbosityLevel)
            {
                _helpText       = helpText;
                _verbosityLevel = verbosityLevel;
            }

            public override bool Matched => false;

            public override string HelpFlags() => string.Empty;

            public override string? HelpText() => _helpText;

            public override int VerbosityLevel() => _verbosityLevel;

            public override bool Match(string arg, bool parseOptions) => false;

            public override bool Parse(string[] argv, ref int i) => true;

            public override bool Positional() => false;

            public override bool Required() => false;

            public override bool HelpOnly() => true;
        }

        private sealed class CmdOptionPositional : CmdOptionInterface
        {
            private readonly string _name;
            private readonly string _helpText;
            private readonly Action<string> _setStorage;
            private readonly int _verbosityLevel;
            private readonly bool _required;
            private bool _matched;

            public CmdOptionPositional(
                string name,
                string helpText,
                Action<string> setStorage,
                int verbosityLevel,
                bool required)
            {
                _name           = name;
                _helpText       = helpText;
                _setStorage     = setStorage;
                _verbosityLevel = verbosityLevel;
                _required       = required;
            }

            public override bool Matched => _matched;

            public override string HelpFlags() => _name;

            public override string? HelpText() => _helpText;

            public override int VerbosityLevel() => _verbosityLevel;

            public override bool Match(string arg, bool parseOptions)
            {
                return !_matched && (!parseOptions || arg.Length == 0 || arg[0] != '-');
            }

            public override bool Parse(string[] argv, ref int i)
            {
                _setStorage(argv[i]);
                i++;
                _matched = true;

                return true;
            }

            public override bool Positional() => true;

            public override bool Required() => _required;

            public override bool HelpOnly() => false;
        }

        private sealed class CmdOptionFlag : CmdOptionInterface
        {
            private readonly char _shortName;
            private readonly string? _longName;
            private readonly string? _metavar;
            private readonly string? _helpText;
            private readonly Func<string, bool>? _parserWithArg;
            private readonly Func<bool>? _parserNoValue;
            private readonly int _verbosityLevel;
            private bool _matched;

            public CmdOptionFlag(
                char shortName,
                string? longName,
                string? metavar,
                string? helpText,
                Func<string, bool>? parserWithArg,
                Func<bool>? parserNoValue,
                int verbosityLevel)
            {
                _shortName      = shortName;
                _longName       = longName;
                _metavar        = metavar;
                _helpText       = helpText;
                _parserWithArg  = parserWithArg;
                _parserNoValue  = parserNoValue;
                _verbosityLevel = verbosityLevel;
            }

            public override bool Matched => _matched;

            public override string HelpFlags()
            {
                StringBuilder ret = new StringBuilder();

                if (_shortName != '\0')
                {
                    ret.Append('-').Append(_shortName);

                    if (_metavar != null)
                    {
                        ret.Append(' ').Append(_metavar);
                    }

                    if (!string.IsNullOrEmpty(_longName))
                    {
                        ret.Append(", ");
                    }
                }

                if (!string.IsNullOrEmpty(_longName))
                {
                    ret.Append("--").Append(_longName);

                    if (_metavar != null)
                    {
                        ret.Append('=').Append(_metavar);
                    }
                }

                return ret.ToString();
            }

            public override string? HelpText() => _helpText;

            public override int VerbosityLevel() => _verbosityLevel;

            public override bool Match(string arg, bool parseOptions)
            {
                return parseOptions && (MatchShort(arg) || MatchLong(arg));
            }

            public override bool Parse(string[] argv, ref int i)
            {
                _matched       = true;
                string current = argv[i];

                if (MatchLong(current) && _longName != null)
                {
                    int prefixLen = 2 + _longName.Length;

                    if (current.Length > prefixLen && current[prefixLen] == '=')
                    {
                        if (_metavar != null && _parserWithArg != null)
                        {
                            i++;

                            return _parserWithArg(current.Substring(prefixLen + 1));
                        }

                        Console.Error.WriteLine($"--{_longName} didn't expect any argument passed to it.");

                        return false;
                    }
                }

                i++;

                if (_metavar != null)
                {
                    if (argv.Length <= i)
                    {
                        Console.Error.WriteLine($"{argv[i - 1]} expected an argument but none passed.");

                        return false;
                    }

                    return _parserWithArg!(argv[i++]);
                }

                return _parserNoValue!();
            }

            public override bool Positional() => false;

            public override bool Required() => false;

            public override bool HelpOnly() => false;

            private bool MatchShort(string arg)
            {
                if (_shortName == '\0' || arg.Length != 2 || arg[0] != '-')
                {
                    return false;
                }

                return arg[1] == _shortName;
            }

            private bool MatchLong(string arg)
            {
                if (string.IsNullOrEmpty(_longName) || !arg.StartsWith("--", StringComparison.Ordinal))
                {
                    return false;
                }

                if (!arg.AsSpan(2).StartsWith(_longName.AsSpan(), StringComparison.Ordinal))
                {
                    return false;
                }

                int endIdx = 2 + _longName.Length;

                return arg.Length == endIdx || arg[endIdx] == '=';
            }
        }
    }

    /// <summary>
    /// Proxy for populating <see cref="ColorHints"/> from command-line <c>key=value</c> pairs,
    /// including reading external ICC/EXIF/XMP/JUMBF files.
    /// Ported from <c>jpegxl::tools::ColorHintsProxy</c> in <c>tools/args.h</c>.
    /// </summary>
    public class ColorHintsProxy
    {
        /// <summary>
        /// Gets the underlying <see cref="ColorHints"/> target instance.
        /// </summary>
        public ColorHints Target { get; } = new ColorHints();

        /// <summary>
        /// Processes a single <c>key=value</c> hint pair and appends it to <see cref="Target"/>.
        /// </summary>
        /// <param name="key">Hint key.</param>
        /// <param name="value">Hint value or file path.</param>
        /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
        public bool Invoke(string key, string value)
        {
            if (key == "icc_pathname")
            {
                if (!FileIo.ReadFile(value, out byte[] icc))
                {
                    return false;
                }

                Target.Add("icc", Encoding.Latin1.GetString(icc));

                return true;
            }

            if (key == "exif" || key == "xmp" || key == "jumbf")
            {
                if (!FileIo.ReadFile(value, out byte[] metadata))
                {
                    return false;
                }

                Target.Add(key, Encoding.Latin1.GetString(metadata));

                return true;
            }

            if (key == "strip")
            {
                Target.Add(value, string.Empty);

                return true;
            }

            Target.Add(key, value);

            return true;
        }
    }

    /// <summary>
    /// Standard command-line option value and flag parsers.
    /// Ported from <c>tools/cmdline.h</c> and <c>tools/args.h</c>.
    /// </summary>
    public static class CmdLineParsers
    {
        /// <summary>
        /// Nested alias for <see cref="JxlSharp.Tools.ColorHintsProxy"/>.
        /// </summary>
        public sealed class ColorHintsProxy : JxlSharp.Tools.ColorHintsProxy
        {
        }

        /// <summary>
        /// Parses a signed 32-bit integer (supporting decimal and <c>0x</c> hex prefixes).
        /// </summary>
        /// <param name="arg">Input string.</param>
        /// <param name="value">Parsed integer.</param>
        /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
        public static bool ParseSigned(string arg, out int value)
        {
            value = 0;

            if (!TryParseBase0Int64(arg, out long parsed) || parsed < int.MinValue || parsed > int.MaxValue)
            {
                Console.Error.WriteLine($"Unable to interpret as signed integer: {arg}.");

                return false;
            }

            value = (int)parsed;

            return true;
        }

        /// <summary>
        /// Parses an unsigned native-sized integer (supporting decimal and <c>0x</c> hex prefixes, rejecting negative signs).
        /// </summary>
        /// <param name="arg">Input string.</param>
        /// <param name="value">Parsed unsigned integer.</param>
        /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
        public static bool ParseUnsigned(string arg, out nuint value)
        {
            value = 0;

            if (!TryParseBase0UInt64(arg, out ulong parsed) || parsed > nuint.MaxValue)
            {
                Console.Error.WriteLine($"Unable to interpret as unsigned integer: {arg}.");

                return false;
            }

            value = (nuint)parsed;

            return true;
        }

        /// <summary>
        /// Parses a signed 64-bit integer.
        /// </summary>
        /// <param name="arg">Input string.</param>
        /// <param name="value">Parsed 64-bit integer.</param>
        /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
        public static bool ParseInt64(string arg, out long value)
        {
            if (!TryParseBase0Int64(arg, out value))
            {
                Console.Error.WriteLine($"Unable to interpret as signed integer: {arg}.");

                return false;
            }

            return true;
        }

        /// <summary>
        /// Parses an unsigned 32-bit integer.
        /// </summary>
        /// <param name="arg">Input string.</param>
        /// <param name="value">Parsed 32-bit unsigned integer.</param>
        /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
        public static bool ParseUint32(string arg, out uint value)
        {
            value = 0;

            if (!ParseUnsigned(arg, out nuint parsed) || parsed > uint.MaxValue)
            {
                return false;
            }

            value = (uint)parsed;

            return true;
        }

        /// <summary>
        /// Parses a single-precision floating-point number.
        /// </summary>
        /// <param name="arg">Input string.</param>
        /// <param name="value">Parsed float.</param>
        /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
        public static bool ParseFloat(string arg, out float value)
        {
            value = 0.0f;

            if (!ParseDouble(arg, out double d))
            {
                Console.Error.WriteLine($"Unable to interpret as float: {arg}.");

                return false;
            }

            value = (float)d;

            return true;
        }

        /// <summary>
        /// Parses a double-precision floating-point number.
        /// </summary>
        /// <param name="arg">Input string.</param>
        /// <param name="value">Parsed double.</param>
        /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
        public static bool ParseDouble(string arg, out double value)
        {
            value = 0.0;

            if (string.IsNullOrWhiteSpace(arg) ||
                !double.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                Console.Error.WriteLine($"Unable to interpret as double: {arg}.");

                return false;
            }

            return true;
        }

        /// <summary>
        /// Copies the argument string into <paramref name="value"/>.
        /// </summary>
        /// <param name="arg">Input string.</param>
        /// <param name="value">Output string.</param>
        /// <returns>Always <c>true</c>.</returns>
        public static bool ParseString(string arg, out string value)
        {
            value = arg ?? string.Empty;

            return true;
        }

        /// <summary>
        /// Sets the boolean output to <c>true</c>.
        /// </summary>
        /// <param name="value">Output boolean.</param>
        /// <returns>Always <c>true</c>.</returns>
        public static bool SetBooleanTrue(out bool value)
        {
            value = true;

            return true;
        }

        /// <summary>
        /// Sets the boolean output to <c>false</c>.
        /// </summary>
        /// <param name="value">Output boolean.</param>
        /// <returns>Always <c>true</c>.</returns>
        public static bool SetBooleanFalse(out bool value)
        {
            value = false;

            return true;
        }

        /// <summary>
        /// Parses <c>"0"</c> or <c>"1"</c> into an <see cref="Override"/> value.
        /// Ported from <c>jpegxl::tools::ParseOverride</c> in <c>tools/args.h</c>.
        /// </summary>
        /// <param name="arg">Input string (<c>"0"</c> or <c>"1"</c>).</param>
        /// <param name="value">Output <see cref="Override"/>.</param>
        /// <returns><c>true</c> if <paramref name="arg"/> is <c>"0"</c> or <c>"1"</c>; otherwise <c>false</c>.</returns>
        public static bool ParseOverride(string arg, out Override value)
        {
            value = Override.Default;

            if (arg == "1")
            {
                value = Override.On;

                return true;
            }

            if (arg == "0")
            {
                value = Override.Off;

                return true;
            }

            Console.Error.WriteLine($"Invalid flag, {arg} must be 0 or 1");

            return false;
        }

        /// <summary>
        /// Parses a <c>key=value</c> string and invokes <paramref name="cb"/>.
        /// Ported from <c>jpegxl::tools::ParseAndAppendKeyValue</c> in <c>tools/args.h</c>.
        /// </summary>
        /// <param name="arg">Argument string containing <c>key=value</c>.</param>
        /// <param name="cb">Callback receiving <c>(key, value)</c>.</param>
        /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
        public static bool ParseAndAppendKeyValue(string arg, Func<string, string, bool> cb)
        {
            if (arg == null || cb == null)
            {
                return false;
            }

            int eqIndex = arg.IndexOf('=');

            if (eqIndex < 0)
            {
                Console.Error.WriteLine($"Expected argument as 'key=value' but received '{arg}'");

                return false;
            }

            string key   = arg.Substring(0, eqIndex);
            string value = arg.Substring(eqIndex + 1);

            return cb(key, value);
        }

        /// <summary>
        /// Parses a <c>key=value</c> string and appends it to <paramref name="proxy"/>.
        /// </summary>
        /// <param name="arg">Argument string containing <c>key=value</c>.</param>
        /// <param name="proxy">Target <see cref="JxlSharp.Tools.ColorHintsProxy"/>.</param>
        /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
        public static bool ParseAndAppendKeyValue(string arg, JxlSharp.Tools.ColorHintsProxy proxy)
        {
            if (proxy == null)
            {
                return false;
            }

            return ParseAndAppendKeyValue(arg, proxy.Invoke);
        }

        private static bool TryParseBase0Int64(string arg, out long result)
        {
            result = 0;

            if (string.IsNullOrWhiteSpace(arg))
            {
                return false;
            }

            ReadOnlySpan<char> trimmed = arg.AsSpan().TrimStart();

            if (trimmed.IsEmpty)
            {
                return false;
            }

            bool negative = false;

            if (trimmed[0] == '-' || trimmed[0] == '+')
            {
                negative = trimmed[0] == '-';
                trimmed  = trimmed.Slice(1);
            }

            if (!TryParseUnsignedDigits(trimmed, out ulong mag))
            {
                return false;
            }

            if (negative)
            {
                ulong maxNeg = (ulong)long.MaxValue + 1UL;

                if (mag > maxNeg)
                {
                    return false;
                }

                result = mag == maxNeg ? long.MinValue : -(long)mag;

                return true;
            }

            if (mag > (ulong)long.MaxValue)
            {
                return false;
            }

            result = (long)mag;

            return true;
        }

        private static bool TryParseBase0UInt64(string arg, out ulong result)
        {
            result = 0;

            if (string.IsNullOrWhiteSpace(arg))
            {
                return false;
            }

            ReadOnlySpan<char> trimmed = arg.AsSpan().TrimStart();

            if (trimmed.IsEmpty || trimmed[0] == '-')
            {
                return false;
            }

            if (trimmed[0] == '+')
            {
                trimmed = trimmed.Slice(1);
            }

            return TryParseUnsignedDigits(trimmed, out result);
        }

        private static bool TryParseUnsignedDigits(ReadOnlySpan<char> span, out ulong result)
        {
            result = 0;

            if (span.IsEmpty)
            {
                return false;
            }

            if (span.Length > 2 && span[0] == '0' && (span[1] == 'x' || span[1] == 'X'))
            {
                return ulong.TryParse(span.Slice(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out result);
            }

            return ulong.TryParse(span, NumberStyles.None, CultureInfo.InvariantCulture, out result);
        }
    }

    /// <summary>
    /// Package version constant for JPEG XL tools.
    /// Ported from <c>tools/tool_version.h</c>.
    /// </summary>
    public static class ToolVersion
    {
        /// <summary>
        /// Package version string (<c>kJpegxlVersion</c>).
        /// </summary>
        public const string KJpegxlVersion = "0.13.0";
    }

    /// <summary>
    /// Codec version and SIMD target configuration string helper.
    /// Ported from <c>tools/codec_config.h</c> and <c>tools/codec_config.cc</c>.
    /// </summary>
    public static class CodecConfig
    {
        /// <summary>
        /// Returns a short string describing the codec version and SIMD targets.
        /// Ported from <c>jpegxl::tools::CodecConfigString</c> in <c>tools/codec_config.cc</c>.
        /// </summary>
        /// <param name="libVersion">Numeric library version (e.g., from <see cref="JxlSharp.Decoder.JxlDecoder.Version"/>).</param>
        /// <returns>Formatted codec configuration string.</returns>
        public static string CodecConfigString(uint libVersion)
        {
            StringBuilder config = new StringBuilder();

            if (libVersion != 0)
            {
                uint major = libVersion / 1000000u;
                uint minor = (libVersion / 1000u) % 1000u;
                uint patch = libVersion % 1000u;

                if (major == 0 && minor == 0 && libVersion >= 0x000100u)
                {
                    major = (libVersion >> 16) & 0xFFu;
                    minor = (libVersion >> 8) & 0xFFu;
                    patch = libVersion & 0xFFu;
                }

                config.AppendFormat(CultureInfo.InvariantCulture, "v{0}.{1}.{2} ", major, minor, patch);
            }

            string version = ToolVersion.KJpegxlVersion;

            if (version != "(unknown)")
            {
                config.Append(version).Append(' ');
            }

            string targetName = Vector.IsHardwareAccelerated
                ? $"_SIMD{Vector<float>.Count * 32}_,Scalar"
                : "_Scalar_";
            config.Append('[').Append(targetName).Append(']');

            return config.ToString();
        }
    }

    /// <summary>
    /// Default pass-through memory manager for command-line tools.
    /// Ported from <c>tools/no_memory_manager.h</c> and <c>tools/no_memory_manager.cc</c>.
    /// </summary>
    public static class NoMemoryManager
    {
        /// <summary>
        /// Gets the default tool <see cref="JxlMemoryManager"/> instance.
        /// </summary>
        public static JxlMemoryManager Instance => Get();

        /// <summary>
        /// Returns the default tool <see cref="JxlMemoryManager"/> instance.
        /// </summary>
        /// <returns>Default <see cref="JxlMemoryManager"/>.</returns>
        public static JxlMemoryManager Get()
        {
            return JxlMemoryManager.Default;
        }
    }

    /// <summary>
    /// Memory manager wrapper that tracks active allocations, peak usage, and memory caps.
    /// Ported from <c>tools/tracking_memory_manager.h</c> and <c>tools/tracking_memory_manager.cc</c>.
    /// </summary>
    public sealed class TrackingMemoryManager
    {
        /// <summary>
        /// One gibibyte (<c>1 &lt;&lt; 30</c> bytes).
        /// </summary>
        public const ulong KGiB = 1UL << 30;

        /// <summary>
        /// One gibibyte (<c>1 &lt;&lt; 30</c> bytes).
        /// </summary>
        public const ulong GiB = KGiB;

        private readonly Dictionary<nint, nuint> _allocations = new Dictionary<nint, nuint>();
        private readonly object _numbersMutex                 = new object();
        private readonly object _mapMutex                     = new object();
        private readonly ulong _cap;
        private readonly ulong _totalCap;
        private readonly JxlMemoryManager _outer;
        private readonly JxlMemoryManager _default;
        private readonly JxlMemoryManager _inner;

        private ulong _bytesInUse;
        private ulong _numAllocations;

        /// <summary>
        /// Initializes a new instance of the <see cref="TrackingMemoryManager"/> class.
        /// </summary>
        /// <param name="cap">Maximum concurrent bytes allowed in use (0 for unlimited).</param>
        /// <param name="totalCap">Maximum cumulative bytes allowed across all allocations (0 for unlimited).</param>
        public TrackingMemoryManager(ulong cap = 0, ulong totalCap = 0)
        {
            _cap      = cap;
            _totalCap = totalCap;
            _default  = new JxlMemoryManager();
            _         = JxlMemoryManager.Init(_default, null);
            _inner    = _default;
            _outer    = new JxlMemoryManager
            {
                Opaque = this,
                Alloc  = AllocCallback,
                Free   = FreeCallback,
            };
        }

        /// <summary>
        /// Gets a value indicating whether an out-of-memory or cap-exceeded condition occurred.
        /// </summary>
        public bool SeenOom { get; private set; }

        /// <summary>
        /// Gets the high-water mark of bytes simultaneously in use.
        /// </summary>
        public ulong MaxBytesInUse { get; private set; }

        /// <summary>
        /// Gets the total number of allocations performed since construction or last <see cref="Reset"/>.
        /// </summary>
        public ulong TotalAllocations { get; private set; }

        /// <summary>
        /// Gets the cumulative number of bytes allocated since construction or last <see cref="Reset"/>.
        /// </summary>
        public ulong TotalBytesAllocated { get; private set; }

        /// <summary>
        /// Returns the <see cref="JxlMemoryManager"/> wrapper that routes allocations through this tracker.
        /// </summary>
        /// <returns>Tracking <see cref="JxlMemoryManager"/>.</returns>
        public JxlMemoryManager Get()
        {
            return _outer;
        }

        /// <summary>
        /// Verifies that no allocations are leaked and resets all counters.
        /// </summary>
        /// <returns><see cref="Status.OkStatus"/> if all allocations were freed; otherwise a failure status.</returns>
        public Status Reset()
        {
            if (_numAllocations != 0)
            {
                return Status.Failure("Memory leak");
            }

            if (_allocations.Count != 0)
            {
                return Status.Failure("Internal logic error");
            }

            if (_bytesInUse != 0)
            {
                return Status.Failure("Internal logic error");
            }

            SeenOom             = false;
            MaxBytesInUse       = 0;
            TotalAllocations    = 0;
            TotalBytesAllocated = 0;

            return true;
        }

        private static nint AllocCallback(object? opaque, nuint size)
        {
            if (opaque is not TrackingMemoryManager self)
            {
                return 0;
            }

            ulong size64 = size;

            lock (self._numbersMutex)
            {
                ulong newTotal = unchecked(self.TotalBytesAllocated + size64);

                if (newTotal < size64 || (self._totalCap != 0 && newTotal > self._totalCap))
                {
                    self.TotalBytesAllocated = self._totalCap;
                    self.SeenOom             = true;

                    return 0;
                }

                ulong newBytesInUse = unchecked(self._bytesInUse + size64);

                if (newBytesInUse < size64 || (self._cap != 0 && newBytesInUse > self._cap))
                {
                    self.SeenOom = true;

                    return 0;
                }

                self._numAllocations++;
                self.TotalAllocations++;
                self._bytesInUse         = newBytesInUse;
                self.MaxBytesInUse       = Math.Max(self.MaxBytesInUse, newBytesInUse);
                self.TotalBytesAllocated = newTotal;
            }

            nint result = JxlMemoryManager.MemoryManagerAlloc(self._inner, size);

            if (result != 0)
            {
                lock (self._mapMutex)
                {
                    self._allocations[result] = size;
                }
            }
            else
            {
                lock (self._numbersMutex)
                {
                    self.SeenOom = true;
                    self._numAllocations--;
                    self._bytesInUse -= size64;
                }
            }

            return result;
        }

        private static void FreeCallback(object? opaque, nint address)
        {
            if (opaque is not TrackingMemoryManager self || address == 0)
            {
                return;
            }

            bool found;
            nuint size;

            lock (self._mapMutex)
            {
                found = self._allocations.Remove(address, out size);
            }

            if (found)
            {
                lock (self._numbersMutex)
                {
                    self._numAllocations--;
                    self._bytesInUse -= size;
                }
            }

            JxlMemoryManager.MemoryManagerFree(self._inner, address);
        }
    }

    /// <summary>
    /// RAII wrapper around a file or standard input/output stream.
    /// Ported from <c>jpegxl::tools::FileWrapper</c> in <c>tools/file_io.h</c>.
    /// </summary>
    public sealed class FileWrapper : IDisposable
    {
        private readonly Stream? _stream;
        private readonly bool _closeOnDelete;
        private readonly long _size = -1;

        /// <summary>
        /// Initializes a new instance of the <see cref="FileWrapper"/> class.
        /// </summary>
        /// <param name="pathname">File path, or <c>"-"</c> for stdin/stdout.</param>
        /// <param name="mode">Open mode (<c>"rb"</c> or <c>"wb"</c>).</param>
        public FileWrapper(string pathname, string mode)
        {
            if (string.IsNullOrEmpty(pathname) || string.IsNullOrEmpty(mode))
            {
                return;
            }

            bool isRead = mode[0] == 'r';

            if (pathname == "-")
            {
                _stream        = isRead ? Console.OpenStandardInput() : Console.OpenStandardOutput();
                _closeOnDelete = false;
                _size          = -1;

                return;
            }

            _closeOnDelete = true;

            try
            {
                FileStream fs = isRead
                    ? new FileStream(pathname, FileMode.Open, FileAccess.Read, FileShare.Read)
                    : new FileStream(pathname, FileMode.Create, FileAccess.Write, FileShare.None);

                _stream = fs;

                if (fs.CanSeek)
                {
                    _size = fs.Length;
                }
            }
            catch (Exception)
            {
                _stream = null;
                _size   = -1;
            }
        }

        /// <summary>
        /// Gets the underlying <see cref="System.IO.Stream"/>, or <c>null</c> if opening failed.
        /// </summary>
        public Stream? Stream => _stream;

        /// <summary>
        /// Gets a value indicating whether the underlying file stream was successfully opened.
        /// </summary>
        public bool IsValid => _stream != null;

        /// <summary>
        /// Gets the size of the file in bytes, or <c>-1</c> if unknown/non-seekable.
        /// </summary>
        public long Size => _size;

        /// <inheritdoc />
        public void Dispose()
        {
            if (_stream != null && _closeOnDelete)
            {
                try
                {
                    _stream.Dispose();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Could not close file\nError: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// File reading and writing helpers supporting regular files and stdin/stdout (<c>"-"</c>).
    /// Ported from <c>tools/file_io.h</c>.
    /// </summary>
    public static class FileIo
    {
        /// <summary>
        /// Reads all bytes from an open <see cref="FileWrapper"/>.
        /// </summary>
        /// <param name="f">Open file wrapper.</param>
        /// <param name="bytes">Outputs the file contents.</param>
        /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
        public static bool ReadFile(FileWrapper f, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();

            if (f == null || !f.IsValid || f.Stream == null)
            {
                return false;
            }

            try
            {
                long size = f.Size;

                if (size >= 0 && size <= Array.MaxLength)
                {
                    byte[] buffer = new byte[(int)size];
                    int offset    = 0;

                    while (offset < buffer.Length)
                    {
                        int read = f.Stream.Read(buffer, offset, buffer.Length - offset);

                        if (read <= 0)
                        {
                            return false;
                        }

                        offset += read;
                    }

                    bytes = buffer;

                    return true;
                }

                using MemoryStream ms = new MemoryStream();
                f.Stream.CopyTo(ms);
                bytes = ms.ToArray();

                return true;
            }
            catch (Exception)
            {
                bytes = Array.Empty<byte>();

                return false;
            }
        }

        /// <summary>
        /// Reads all bytes from <paramref name="filename"/> (or stdin if <c>"-"</c>).
        /// </summary>
        /// <param name="filename">File path or <c>"-"</c>.</param>
        /// <param name="bytes">Outputs the file contents.</param>
        /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
        public static bool ReadFile(string filename, out byte[] bytes)
        {
            using FileWrapper f = new FileWrapper(filename, "rb");

            return ReadFile(f, out bytes);
        }

        /// <summary>
        /// Writes <paramref name="bytes"/> to <paramref name="filename"/> (or stdout if <c>"-"</c>).
        /// </summary>
        /// <param name="filename">File path or <c>"-"</c>.</param>
        /// <param name="bytes">Bytes to write.</param>
        /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
        public static bool WriteFile(string filename, ReadOnlySpan<byte> bytes)
        {
            using FileWrapper file = new FileWrapper(filename, "wb");

            if (!file.IsValid || file.Stream == null)
            {
                Console.Error.WriteLine($"Could not open {filename} for writing");

                return false;
            }

            try
            {
                file.Stream.Write(bytes);
                file.Stream.Flush();

                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Could not write to file\nError: {ex.Message}");

                return false;
            }
        }
    }

    /// <summary>
    /// Collects elapsed timings across benchmark repetitions and computes MP/s and MB/s statistics.
    /// Ported from <c>jpegxl::tools::SpeedStats</c> in <c>tools/speed_stats.h</c> and <c>tools/speed_stats.cc</c>.
    /// </summary>
    public sealed class SpeedStats
    {
        private readonly List<double> _elapsed = new List<double>();
        private nuint _xsize;
        private nuint _ysize;
        private nuint _fileSize;

        /// <summary>
        /// Statistical summary of elapsed times across runs.
        /// </summary>
        public struct Summary
        {
            /// <summary>
            /// Description of how <see cref="CentralTendency"/> was computed (e.g., <c>""</c>, <c>"second: "</c>, <c>"geomean: "</c>, <c>"median: "</c>).
            /// </summary>
            public string Type;

            /// <summary>
            /// Central tendency elapsed time in seconds.
            /// </summary>
            public double CentralTendency;

            /// <summary>
            /// Minimum elapsed time in seconds.
            /// </summary>
            public double Min;

            /// <summary>
            /// Maximum elapsed time in seconds.
            /// </summary>
            public double Max;

            /// <summary>
            /// Variability measure (0 unless median fallback is used).
            /// </summary>
            public double Variability;
        }

        /// <summary>
        /// Records a positive elapsed duration in seconds.
        /// </summary>
        /// <param name="elapsedSeconds">Elapsed time in seconds.</param>
        public void NotifyElapsed(double elapsedSeconds)
        {
            if (elapsedSeconds > 0.0)
            {
                _elapsed.Add(elapsedSeconds);
            }
        }

        /// <summary>
        /// Computes the timing summary across all recorded runs. May sort internal timings.
        /// </summary>
        /// <param name="summary">Computed timing summary.</param>
        /// <returns><c>true</c> if at least one timing was recorded; otherwise <c>false</c>.</returns>
        public bool GetSummary(out Summary summary)
        {
            summary = default;

            if (_elapsed.Count == 0)
            {
                return false;
            }

            ComputeMinMax(out summary.Min, out summary.Max);

            if (_elapsed.Count == 1)
            {
                summary.CentralTendency = _elapsed[0];
                summary.Variability     = 0.0;
                summary.Type            = string.Empty;

                return true;
            }

            if (_elapsed.Count == 2)
            {
                summary.CentralTendency = _elapsed[1];
                summary.Variability     = 0.0;
                summary.Type            = "second: ";

                return true;
            }

            if (TryComputeGeomean(ref summary))
            {
                return true;
            }

            _elapsed.Sort();
            summary.CentralTendency = _elapsed[_elapsed.Count / 2];
            double stdev            = 0.0;

            foreach (double t in _elapsed)
            {
                double diff = t - summary.CentralTendency;
                stdev      += diff * diff;
            }

            summary.Variability = Math.Sqrt(stdev);
            summary.Type        = "median: ";

            return true;
        }

        /// <summary>
        /// Sets the image dimensions to enable MP/s calculation.
        /// </summary>
        /// <param name="xsize">Width in pixels.</param>
        /// <param name="ysize">Height in pixels.</param>
        public void SetImageSize(nuint xsize, nuint ysize)
        {
            _xsize = xsize;
            _ysize = ysize;
        }

        /// <summary>
        /// Sets the byte size of the file to enable MB/s calculation.
        /// </summary>
        /// <param name="fileSize">File size in bytes.</param>
        public void SetFileSize(nuint fileSize)
        {
            _fileSize = fileSize;
        }

        /// <summary>
        /// Computes the summary and prints megapixels/sec and megabytes/sec to standard error.
        /// </summary>
        /// <param name="workerThreads">Number of worker threads to report.</param>
        /// <returns><c>true</c> if summary statistics were available and printed; otherwise <c>false</c>.</returns>
        public bool Print(nuint workerThreads)
        {
            return Print(workerThreads, Console.Error);
        }

        /// <summary>
        /// Computes the summary and prints megapixels/sec and megabytes/sec to the specified <paramref name="writer"/>.
        /// </summary>
        /// <param name="workerThreads">Number of worker threads to report.</param>
        /// <param name="writer">Destination text writer.</param>
        /// <returns><c>true</c> if summary statistics were available and printed; otherwise <c>false</c>.</returns>
        public bool Print(nuint workerThreads, TextWriter writer)
        {
            if (!GetSummary(out Summary s))
            {
                return false;
            }

            TextWriter target = writer ?? Console.Error;
            string mpsStats   = SummaryStat((double)_xsize * _ysize * 1e-6, "MP", in s, ", ");
            string mbsStats   = SummaryStat(_fileSize * 1e-6, "MB", in s, ", ");
            int reps          = _elapsed.Count;
            string repsStr    = reps > 1
                ? string.Create(CultureInfo.InvariantCulture, $", {reps} reps")
                : string.Empty;

            target.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{(int)_xsize} x {(int)_ysize}{mpsStats}{mbsStats}{repsStr}, {(int)workerThreads} threads."));

            return true;
        }

        private static string SummaryStat(double value, string unit, in Summary s, string prefix)
        {
            if (value == 0.0)
            {
                return string.Empty;
            }

            double valueTendency = value / s.CentralTendency;
            double valueMin      = value / s.Max;
            double valueMax      = value / s.Min;

            string variability = string.Empty;

            if (s.Variability != 0.0)
            {
                double stdev = value / s.Variability;
                variability  = string.Format(CultureInfo.InvariantCulture, " (stdev {0:F3})", stdev);
            }

            string range = string.Empty;

            if (s.Min != s.Max)
            {
                range = string.Format(CultureInfo.InvariantCulture, " [{0:F3}, {1:F3}]", valueMin, valueMax);
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}{1}{2:F3} {3}/s{4}{5}",
                prefix,
                s.Type,
                valueTendency,
                unit,
                range,
                variability);
        }

        private void ComputeMinMax(out double min, out double max)
        {
            min = _elapsed[0];
            max = _elapsed[0];

            for (int i = 1; i < _elapsed.Count; i++)
            {
                double v = _elapsed[i];

                if (v < min)
                {
                    min = v;
                }

                if (v > max)
                {
                    max = v;
                }
            }
        }

        private bool TryComputeGeomean(ref Summary summary)
        {
            if (Math.Pow(_elapsed[0], _elapsed.Count) >= 1e100)
            {
                return false;
            }

            double product = 1.0;

            for (int i = 1; i < _elapsed.Count; i++)
            {
                product *= _elapsed[i];
            }

            summary.CentralTendency = Math.Pow(product, 1.0 / (_elapsed.Count - 1));
            summary.Variability     = 0.0;
            summary.Type            = "geomean: ";

            return double.IsNormal(summary.CentralTendency);
        }
    }
}
