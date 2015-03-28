using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using RT.Util;
using RT.Util.CommandLine;
using RT.Util.Consoles;
using RT.Util.ExtensionMethods;

namespace OldFiles
{
    class Program
    {
        private static CommandLine Args;
        private static bool HadProblems = false;
        private static DateTime Now;

        private static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--post-build-check")
                return Ut.RunPostBuildChecks(args[1], Assembly.GetExecutingAssembly());

            Args = CommandLineParser.ParseOrWriteUsageToConsole<CommandLine>(args);
            if (Args == null)
                return 1;

            Now = DateTime.Now; // so that it's fixed throughout the entire run
            if (Args.Recursive)
                processDir(findAllFilesRecursive());
            else
                foreach (var dir in Args.Dirs)
                    processDir(new DirectoryInfo(dir).GetFiles());

            if (HadProblems)
                ConsoleUtil.WriteLine("Warning: ".Color(ConsoleColor.Red) + "some errors have occurred during this run. Please review the stderr output for details.");

            return HadProblems ? 1 : 0;
        }

        private static IEnumerable<FileInfo> findAllFilesRecursive()
        {
            var result = new List<FileInfo>();
            foreach (var dir in Args.Dirs)
                addDir(new DirectoryInfo(dir), result);
            return result;
        }

        private static void addDir(DirectoryInfo dir, List<FileInfo> files)
        {
            try
            {
                files.AddRange(dir.GetFiles());
                foreach (var d in dir.GetDirectories())
                    addDir(d, files);
            }
            catch (UnauthorizedAccessException)
            {
                ConsoleUtil.WriteLine("Error: ".Color(ConsoleColor.Red) + "not authorized to list directory contents: " + dir.FullName, stdErr: true);
                HadProblems = true;
            }
            catch (Exception e)
            {
                ConsoleUtil.WriteLine("Error: ".Color(ConsoleColor.Red) + dir.FullName + ": " + e.Message, stdErr: true);
                HadProblems = true;
            }
        }

        private class info
        {
            public FileInfo File;
            public DateTime Timestamp;
            public string BareName;
            public ConsoleColoredString ColoredName;
            public bool Old = false;
            public double Age { get { return (Now - Timestamp).TotalDays; } }

            public info(FileInfo file, Match match)
            {
                File = file;
                var groups = new[] { match.Groups["y"], match.Groups["m"], match.Groups["d"], match.Groups["th"], match.Groups["tm"], match.Groups["ts"] }.AsEnumerable();
                groups = groups.Where(g => g.Success).OrderByDescending(g => g.Index);
                ColoredName = File.Name.Color(ConsoleColor.DarkGray);
                foreach (var group in groups)
                    ColoredName = ColoredName.ColorSubstring(group.Index, group.Length, ConsoleColor.White);
                if (match.Groups["g"].Success)
                {
                    var bareName = new StringBuilder();
                    foreach (Capture capture in match.Groups["g"].Captures)
                    {
                        bareName.Append(capture.Value);
                        ColoredName = ColoredName.ColorSubstring(capture.Index, capture.Length, ConsoleColor.Yellow);
                    }
                    BareName = bareName.ToString();
                }
                else
                {
                    var bareName = new StringBuilder(File.Name);
                    foreach (var group in groups)
                        bareName.Remove(group.Index, group.Length);
                    BareName = bareName.ToString();
                }
                ColoredName = (File.FullName.Substring(0, File.FullName.Length - ColoredName.Length)).Color(ConsoleColor.DarkGray) + ColoredName;
            }
        }

        private static void processDir(IEnumerable<FileInfo> files)
        {
            if (Args.FilterRegex != null)
                files = files.Where(f => Args.FilterRegex.IsMatch(f.FullName));
            var groups = files
                .Select(file => new { file, match = Args.TimestampFormatRegex.Match(file.Name) })
                .Where(f => f.match.Success)
                .Select(f => new info(f.file, f.match) { Timestamp = getTimestamp(f.file, f.match) })
                .Where(f => f.Timestamp != default(DateTime))
                .GroupBy(f => f.BareName);
            foreach (var group in groups)
            {
                if (Args.Verbose)
                    ConsoleUtil.WriteLine("Group: ".Color(ConsoleColor.Cyan) + group.Key);
                // Apply max age
                foreach (var file in group.Where(f => f.Age > Args.MaxAge))
                    file.Old = true;
                // Apply spacing
                var remaining = group.Where(f => !f.Old).OrderByDescending(f => f.Age).ToList();
                var prev = remaining.Count > 0 ? remaining[0] : null;
                for (int i = 1; i < remaining.Count - 1; i++)
                {
                    var gap = prev.Age - remaining[i + 1].Age;
                    var wantedGap = Args.SpacingFunc(remaining[i + 1].Age);
                    if (gap <= wantedGap)
                        remaining[i].Old = true;
                    else
                        prev = remaining[i];
                }
                // Now actually process the old files as required
                foreach (var file in group.OrderByDescending(f => f.Age))
                {
                    if (Args.Verbose || file.Old)
                    {
                        var dbg = "";
#if DEBUG
                        try { dbg = ", gap {0:0.0}, wanted {1:0.0}".Fmt(file.Age - group.Where(f => !f.Old && f.Age < file.Age).Max(f => f.Age), Args.SpacingFunc(file.Age)); }
                        catch { }
#endif
                        ConsoleUtil.Write("  " + file.ColoredName + ", ");
                        ConsoleUtil.Write(file.Age.ToString("0.0") + " days old, ");
                        ConsoleUtil.Write(file.Old ? "remove".Color(ConsoleColor.Red) : "keep".Color(ConsoleColor.Green));
                        Console.WriteLine(dbg);
                    }
                    if (!file.Old)
                        continue;
                    bool executeOK = true;
                    if (Args.Execute != null)
                    {
                        var runner = new CommandRunner();
                        runner.Command = Args.Execute.Replace("{}", file.File.FullName);
                        if (Args.Verbose)
                        {
                            runner.StdoutText += str => Console.Write(str);
                            runner.StderrText += str => Console.Error.Write(str);
                        }
                        runner.Start();
                        runner.EndedWaitHandle.WaitOne();
                        executeOK = runner.ExitCode == 0;
                        if (Args.Verbose)
                            Console.WriteLine();
                    }
                    if (Args.Delete && executeOK)
                        try { file.File.Delete(); }
                        catch (FileNotFoundException) { }
                        catch
                        {
                            ConsoleUtil.WriteLine("Error: ".Color(ConsoleColor.Red) + "could not delete file: " + file.File.FullName, stdErr: true);
                            HadProblems = true;
                        }
                }
            }
        }

        private static DateTime getTimestamp(FileInfo file, Match match)
        {
            if (!match.Groups["y"].Success && !match.Groups["m"].Success && !match.Groups["d"].Success)
                return default(DateTime); // no warning in this case; the file simply doesn't have a timestamp
            if (!match.Groups["y"].Success || !match.Groups["m"].Success || !match.Groups["d"].Success)
            {
                ConsoleUtil.WriteLine("Error: ".Color(ConsoleColor.Red) + "the {field}TimestampFormat{} matches a year, a month or a day, but not all of them. File {0}".Fmt(
                    file.FullName), stdErr: true);
                HadProblems = true;
                return default(DateTime);
            }
            bool hasH = match.Groups["th"].Success;
            bool hasM = match.Groups["tm"].Success;
            bool hasS = match.Groups["ts"].Success;
            if ((hasS && !hasM) || (hasM && !hasH))
            {
                ConsoleUtil.WriteLine("Error: ".Color(ConsoleColor.Red) + "the {field}TimestampFormat{} matches seconds but not minutes, or minutes but not hours. File {0}".Fmt(
                    file.FullName), stdErr: true);
                HadProblems = true;
                return default(DateTime);
            }
            try
            {
                var y = int.Parse(match.Groups["y"].Value);
                var m = int.Parse(match.Groups["m"].Value);
                var d = int.Parse(match.Groups["d"].Value);
                if (y < 100)
                    y = (y < 80 ? 2000 : 1900) + y;
                var th = hasH ? int.Parse(match.Groups["th"].Value) : 0;
                var tm = hasM ? int.Parse(match.Groups["tm"].Value) : 0;
                var ts = hasS ? int.Parse(match.Groups["ts"].Value) : 0;
                return new DateTime(y, m, d, th, tm, ts);
            }
            catch
            {
                var time = (hasH ? match.Groups["th"].Value : "") + (hasM ? ":" + match.Groups["tm"].Value : "") + (hasS ? ":" + match.Groups["ts"].Value : "");
                ConsoleUtil.WriteLine("Error: ".Color(ConsoleColor.Red) + "could not parse the timestamp ({0}) for file {1}".Fmt(
                    "y {0}, m {1}, d {2}".Fmt(match.Groups["y"].Value, match.Groups["m"].Value, match.Groups["d"].Value) + (time == "" ? "" : (", time " + time)),
                    file.FullName), stdErr: true);
                HadProblems = true;
                return default(DateTime);
            }
        }

#if DEBUG
        private static void PostBuildCheck(IPostBuildReporter rep)
        {
            CommandLineParser.PostBuildStep<CommandLine>(rep, null);
        }
#endif
    }
}
