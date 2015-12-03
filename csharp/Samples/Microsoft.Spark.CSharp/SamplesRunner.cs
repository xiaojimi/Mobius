﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Microsoft.Spark.CSharp.Samples
{
    //finds all methods that are marked with [Sample] attribute and 
    //runs all of them if sparkclr.samples.torun commandline arg is not used
    //or just runs the ones that are provided as comma separated list
    internal class SamplesRunner
    {
        private static Regex samplesToRunRegex;
        private static Regex samplesCategoryRegex;
        private static Stopwatch stopWatch;
        // track <SampleName, Category, SampleSucceeded, Duration> for reporting
        private static readonly List<Tuple<string, string, bool, TimeSpan>> samplesRunResultList = new List<Tuple<string, string, bool, TimeSpan>>();

        internal static void RunSamples()
        {
            var samples = Assembly.GetEntryAssembly().GetTypes()
                      .SelectMany(type => type.GetMethods(BindingFlags.NonPublic | BindingFlags.Static))
                      .Where(method => method.GetCustomAttributes(typeof(SampleAttribute), false).Length > 0)
                      .OrderByDescending(method => method.Name);
        
            samplesToRunRegex = GetRegex();
            samplesCategoryRegex = GetCategoryRegex();
            stopWatch = Stopwatch.StartNew();

            foreach (var sample in samples)
            {
                var sampleRunResult = RunSample(sample);
                if (sampleRunResult != null)
                {
                    samplesRunResultList.Add(sampleRunResult);
                }
            }
            stopWatch.Stop();
            ReportOutcome();
        }

        private static Tuple<string, string, bool, TimeSpan> RunSample(MethodInfo sample)
        {
            var sampleName = sample.Name;
            var sampleAttributes = (SampleAttribute[]) sample.GetCustomAttributes(typeof(SampleAttribute), false);
            var categoryNames = string.Join<SampleAttribute>(",", sampleAttributes);

            if (samplesCategoryRegex != null)
            {
                if (!sampleAttributes.Any(attribute => attribute.Match(samplesCategoryRegex)))
                {
                    return null;
                }
            }

            if (samplesToRunRegex != null)
            {
                if ((SparkCLRSamples.Configuration.SamplesToRun.IndexOf(sampleName, StringComparison.InvariantCultureIgnoreCase) < 0)
                    //assumes method/sample names are unique
                    && !samplesToRunRegex.IsMatch(sampleName))
                {
                    return null;
                }
            }

            var clockStart = stopWatch.Elapsed;
            var duration = stopWatch.Elapsed - clockStart;
            try
            {
                if (!SparkCLRSamples.Configuration.IsDryrun)
                {
                    Console.WriteLine("----- Running sample {0} -----", sampleName);
                    sample.Invoke(null, new object[] { });
                    duration = stopWatch.Elapsed - clockStart;
                    Console.WriteLine("----- Finished running sample {0}, duration={1} -----", sampleName, duration);
                }

                return new Tuple<string, string, bool, TimeSpan>(sampleName, categoryNames, true, duration);
            }
            catch (Exception ex)
            {
                duration = stopWatch.Elapsed - clockStart;
                Console.WriteLine("----- Error running sample {0} -----{1}{2}, duration={3}",
                    sampleName, Environment.NewLine, ex, duration);
                return new Tuple<string, string, bool, TimeSpan>(sampleName, categoryNames, false, duration);
            }

        }

        private static Regex GetCategoryRegex()
        {
            Regex categoryRegex = null;
            if (!string.IsNullOrEmpty(SparkCLRSamples.Configuration.SamplesCategory))
            {
                var s = SparkCLRSamples.Configuration.SamplesCategory;
                if (s.StartsWith("/") && s.EndsWith("/") && s.Length > 2)
                {
                    // forward-slashes enclose .Net regular expression 
                    categoryRegex = new Regex(s.Substring(1, s.Length - 2));
                }
                else
                {
                    // default to Unix or Windows command line wild card matching, case insensitive
                    categoryRegex = new Regex("^" + Regex.Escape(s).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                        RegexOptions.IgnoreCase);
                }
            }
            return categoryRegex;
        }

        private static Regex GetRegex()
        {
            Regex regex = null;
            if (!string.IsNullOrEmpty(SparkCLRSamples.Configuration.SamplesToRun))
            {
                var s = SparkCLRSamples.Configuration.SamplesToRun;
                if (s.StartsWith("/") && s.EndsWith("/") && s.Length > 2)
                {
                    // forward-slashes enclose .Net regular expression 
                    regex = new Regex(s.Substring(1, s.Length - 2));
                }
                else
                {
                    // default to Unix or Windows command line wild card matching, case insensitive
                    regex = new Regex("^" + Regex.Escape(s).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                        RegexOptions.IgnoreCase);
                }
            }
            return regex;
        }

        private static void ReportOutcome()
        {
            var msg = new StringBuilder();

            msg.Append("----- ")
                .Append("Finished running ")
                .Append(string.Format("{0} samples(s)", samplesRunResultList.Count))
                .Append(" in ").Append(stopWatch.Elapsed)
                .AppendLine(" -----");

            var completed = samplesRunResultList.Where(x => x.Item3).ToList();
            var errors = samplesRunResultList.Where(x => !x.Item3).ToList();

            msg.Append("----- ")
                .Append(" Completion counts:")
                .Append(" Success=").Append(completed.Count)
                .Append(" Failed=").Append(errors.Count)
                .AppendLine(" -----");

            msg.AppendLine("Successful samples:");
            foreach (var s in completed)
            {
                msg.Append("    ").AppendLine(string.Format("{0} (category: {1}), duration={2}", s.Item1, s.Item2, s.Item3));
            }

            msg.AppendLine("Failed samples:");
            foreach (var s in errors)
            {
                msg.Append("    ").AppendLine(string.Format("{0} (category: {1}), duration={2}", s.Item1, s.Item2, s.Item3));
            }

            if (errors.Count == 0)
            {
                Console.WriteLine(msg.ToString());
            }
            else
            {
                Console.WriteLine("[Warning]{0}", msg);
            }
        }
    }
}
