using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RT.Util;
using RT.Util.CommandLine;
using RT.Util.Consoles;
using RT.Util.ExtensionMethods;

namespace OldFiles
{
    sealed class CommandLine : ICommandLineValidatable
    {
#pragma warning disable 0649 // Field is never assigned to

        [DocumentationRhoML("{h}Restrict analysis to files whose names match this regular expression.{}")]
        [Option("-f", "--filter")]
        public string Filter;

        [DocumentationRhoML("{h}Recursively process subdirectories.{}\nWhen specified, all files in all directories are grouped by name and considered together. When omitted, each folder passed to {field}Folders{} is considered separately from other folders.")]
        [Option("-r", "--recursive")]
        public bool Recursive;

        [DocumentationRhoML("{h}All files older than {field}Age{} should be deemed old.{}\nSpecified in days; fractions are permitted.")]
        [Option("-m", "--max-age")]
        public double MaxAge = double.MaxValue;

#warning Expand documentation here
        [DocumentationRhoML("{h}All files spaced more closely than specified in {field}Spacing{} should be deemed old.{}")]
        [Option("-s", "--spacing")]
        public string Spacing;

        [DocumentationRhoML("{h}A regular expression specifying how timestamps should be parsed.{}\nThe regex must contain named groups {h}y{}, {h}m{}, {h}d{}, and may also contain groups {h}th{}, {h}tm{}, {h}ts{}. Only files whose names match this regex are analysed. Matched parts are highlighted white in the verbose output. The default format matches timestamps like {h}\"YYYY-MM-DD\"{}, {h}\"YYYY-MM-DD.hh-mm-ss\"{} and {h}\"YYYY-MM-DD hhmmss\"{} (seconds optional).\nIf a group named {h}g{} is present, the matched string is used to group the files. Multiple groups may be named {h}g{} in the same regex if necessary. The matched part is highlighted yellow in the verbose output.")]
        [Option("-t", "--timestamp")]
        public string TimestampFormat;

        [DocumentationRhoML("{h}Print extra information.{}\nIf specified, OldFiles will list the names of files that aren't deemed old, file group names, and will display the output of the {option}--execute{} command, if any, for each old file.")]
        [Option("-v", "--verbose")]
        public bool Verbose;

        [DocumentationRhoML("{h}Delete all files that are deemed old.{}")]
        [Option("-d", "--delete")]
        public bool Delete;

        [DocumentationRhoML("{h}Execute the specified OS command for all files that are deemed old.{}\nThe command is executed once for each file, and is formed by replacing the string \"{h}{{}{}\" with the file path. Where {option}--delete{} is also specified, the file will be deleted only if this command exits with the status code 0.")]
        [Option("-e", "--execute")]
        public string Execute;

        [DocumentationRhoML("{h}The folder(s) to be scanned for old files.{}")]
        [IsPositional, IsMandatory]
        public string[] Folders;

        [Ignore]
        public Regex FilterRegex;

        [Ignore]
        public Func<double, double> SpacingFunc;

        [Ignore]
        public Regex TimestampFormatRegex;

#pragma warning restore 0649 // Field is never assigned to

        public ConsoleColoredString Validate()
        {
            var result = validate();
            if (result == null)
                return null;
            else
                return CommandLineParser.Colorize(RhoML.Parse(result));
        }

        private string validate()
        {
            if (Filter != null)
            {
                try { FilterRegex = new Regex(Filter, RegexOptions.IgnoreCase | RegexOptions.Singleline); }
                catch (Exception e) { return "The value you provided for {field}Filter{} is not a valid regular expression: {0}".Fmt(RhoML.Escape(e.Message)); }
            }

            if (TimestampFormat == null)
                TimestampFormatRegex = new Regex(@"(?<y>\d\d\d\d)-(?<m>\d\d)-(?<d>\d\d)   ( (\.+(?<th>\d\d)-(?<tm>\d\d)(-(?<ts>\d\d))?)  |  (\s+(?<th>\d\d)(?<tm>\d\d)(?<ts>\d\d)?) )?", RegexOptions.Singleline | RegexOptions.IgnorePatternWhitespace);
            else
            {
                try { TimestampFormatRegex = new Regex(TimestampFormat, RegexOptions.IgnoreCase | RegexOptions.Singleline); }
                catch (Exception e) { return "The value you provided for {field}TimestampFormat{} is not a valid regular expression: {0}".Fmt(RhoML.Escape(e.Message)); }
                var names = TimestampFormatRegex.GetGroupNames();
                if (!names.Contains("y") || !names.Contains("m") || !names.Contains("d"))
                    return @"The regular expression for {field}TimestampFormat{} must include the named capture groups ""y"", ""m"" and ""d"" for year, month and day.";
                if ((names.Contains("tm") && !names.Contains("th")) || (names.Contains("ts") && !names.Contains("tm")))
                    return @"The regular expression for {field}TimestampFormat{} may only include the named capture group ""tm"" only if ""th"" is also present. Similarly, the group ""ts"" requires ""tm"".";
            }

            if (Spacing == null)
                SpacingFunc = age => 0;
            else
            {
                if (Spacing.StartsWith("fixed:", StringComparison.OrdinalIgnoreCase))
                {
                    // --spacing fixed:15
                    var value = Spacing.Substring(6);
                    double spacing;
                    if (!double.TryParse(value, out spacing))
                        return @"Cannot parse the {field}Spacing{} parameter: the {h}fixed{} value should be a number.";
                    SpacingFunc = age => spacing;
                }
                else if (Spacing.StartsWith("list:", StringComparison.OrdinalIgnoreCase))
                {
                    // --spacing list:[5,0.15age][15,2]
                    var value = Spacing.Substring(5);
                    var matches = Regex.Matches(value, @"\[(?<limit>[\d.]+)\,(?<value>[\d.]+)(?<rel>age)?\]", RegexOptions.Singleline | RegexOptions.ExplicitCapture).Cast<Match>();
                    // Make sure there are no extraneous characters
                    int cur = 0;
                    foreach (var match in matches)
                    {
                        if (match.Index != cur)
                            return @"Cannot parse the {field}Spacing{} parameter. Extraneous characters in the {h}list{} specifier: ""{0}""".Fmt(RhoML.Escape(value.Substring(cur, match.Index - cur)));
                        cur = match.Index + match.Length;
                    }
                    if (cur != value.Length)
                        return @"Cannot parse the {field}Spacing{} parameter. Extraneous characters in the {h}list{} specifier: ""{0}""".Fmt(RhoML.Escape(value.Substring(cur)));
                    // Construct the spacing function
                    var list = matches
                        .Select(m => new { Limit = double.Parse(m.Groups["limit"].Value), Val = double.Parse(m.Groups["value"].Value), Relative = m.Groups["rel"].Success })
                        .OrderBy(m => m.Limit)
                        .ToArray();
                    SpacingFunc = age =>
                    {
                        var relevant = list.FirstOrDefault(v => v.Limit <= age);
                        if (relevant == null)
                            return 0; // if there is no entry with a limit of 0, all files younger than the first limit are never considered old.
                        return relevant.Relative ? (relevant.Val * age) : (relevant.Val);
                    };
                }
                else
                    return @"Cannot parse the {field}Spacing{} parameter.";
            }

            foreach (var folder in Folders)
            {
                if (!Directory.Exists(folder))
                    return @"Folder not found: {h}{0}{}".Fmt(RhoML.Escape(folder));
            }

            return null;
        }
    }
}
