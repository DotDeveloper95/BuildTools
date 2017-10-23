// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KoreBuild.Tasks.Policies;
using KoreBuild.Tasks.ProjectModel;
using Microsoft.AspNetCore.BuildTools;
using Microsoft.Build.Framework;
using Newtonsoft.Json;

namespace KoreBuild.Tasks
{
    /// <summary>
    /// Applies policies to a repository on how NuGet can be used, such as restricting which PackageReference's are allowed,
    /// which versions of packages can be used, which feeds are available, etc.
    /// </summary>
    public class ApplyNuGetPolicies : Microsoft.Build.Utilities.Task, ICancelableTask
    {
        private readonly CancellationTokenSource _cts;
        private readonly MSBuildPolicyFactory _policyFactory;

        public ApplyNuGetPolicies()
        {
            _policyFactory = new MSBuildPolicyFactory();
            _cts = new CancellationTokenSource();
        }

        [Required]
        public string SolutionDirectory { get; set; }

        /// <summary>
        /// The policies to be applied to the repository.
        /// </summary>
        [Required]
        public ITaskItem[] Policies { get; set; }

        /// <summary>
        /// The MSBuild projects or solutions to which the policy should be applied.
        /// </summary>
        [Required]
        public ITaskItem[] Projects { get; set; }

        /// <summary>
        /// Key-value base list of properties to be applied to <see cref="Projects" /> during project evaluation.
        /// e.g. "Configuration=Debug;BuildNumber=1234"
        /// </summary>
        public string[] ProjectProperties { get; set; }

        /// <summary>
        /// NuGet sources. When set, they override sources from NuGet.config.
        /// </summary>
        public string[] RestoreSources { get; set; }

        /// <summary>
        /// NuGet sources.
        /// When set they are in addition to source from NuGet.config and/or <see cref="RestoreSources"/>.
        /// </summary>
        public string[] RestoreAdditionalSources { get; set; }

        /// <summary>
        /// User packages folder
        /// </summary>
        public string RestorePackagesPath { get; set; }

        /// <summary>
        /// Disable parallel project restores and downloads
        /// </summary>
        public bool RestoreDisableParallel { get; set; }

        /// <summary>
        /// NuGet.Config path
        /// </summary>
        public string RestoreConfigFile { get; set; }

        /// <summary>
        /// Disable the web cache
        /// </summary>
        public bool RestoreNoCache { get; set; }

        /// <summary>
        /// Ignore errors from package sources
        /// </summary>
        public bool RestoreIgnoreFailedSources { get; set; }

        public void Cancel()
        {
            _cts.Cancel();
        }

        public override bool Execute()
        {
            return ExecuteAsync().GetAwaiter().GetResult();
        }

        private async Task<bool> ExecuteAsync()
        {
            if (_cts.IsCancellationRequested)
            {
                return false;
            }

            var policies = CreatePolicies();
            if (policies.Count == 0)
            {
                return !Log.HasLoggedErrors;
            }

            var projects = new ProjectInfoFactory(Log).CreateMany(Projects, ProjectProperties, true, _cts.Token);

            if (_cts.IsCancellationRequested)
            {
                return false;
            }

            var context = new PolicyContext
            {
                SolutionDirectory = SolutionDirectory,
                Projects = projects,
                Log = Log,
                RestoreSources = RestoreSources?.ToList() ?? new List<string>(),
                RestoreAdditionalSources = RestoreAdditionalSources?.ToList() ?? new List<string>(),
                RestorePackagesPath = RestorePackagesPath,
                RestoreDisableParallel = RestoreDisableParallel,
                RestoreConfigFile = RestoreConfigFile,
                RestoreNoCache = RestoreNoCache,
                RestoreIgnoreFailedSources = RestoreIgnoreFailedSources,
            };

            foreach (var policy in policies)
            {
                if (_cts.IsCancellationRequested)
                {
                    return false;
                }

                try
                {
                    await policy.ApplyAsync(context, _cts.Token);
                }
                catch (Exception ex)
                {
                    Log.LogKoreBuildError(KoreBuildErrors.PolicyFailedToApply, $"Unexpected error when applying package policy: {policy.GetType().Name}." + Environment.NewLine, ex.ToString());
                    return false;
                }
            }

            var verifierProps = Path.Combine(
                Path.GetDirectoryName(typeof(ApplyNuGetPolicies).Assembly.Location),
                "Policy.VerifyImport.props");

            if (_cts.IsCancellationRequested)
            {
                return false;
            }

            foreach (var project in context.Projects)
            {
                project.TargetsExtension.AddImport(verifierProps, required: false);
                project.TargetsExtension.Save();
            }

            return !Log.HasLoggedErrors;
        }

        internal IList<INuGetPolicy> CreatePolicies()
        {
            var policies = new List<INuGetPolicy>();
            foreach (var policyItems in Policies.GroupBy(g => g.GetMetadata("PolicyType") ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                var type = policyItems.Key;
                var policy = _policyFactory.Create(type, policyItems, Log);
                if (policy == null)
                {
                    Log.LogKoreBuildError(KoreBuildErrors.UnknownPolicyType, $"Unrecognized package policy type: '{policyItems.Key}'");
                    Cancel();
                    continue;
                }

                policies.Add(policy);
            }

            return policies;
        }
    }
}
