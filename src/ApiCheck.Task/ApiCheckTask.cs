// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Reflection;
using Microsoft.Build.Framework;

namespace Microsoft.AspNetCore.BuildTools.ApiCheck.Task
{
    /// <summary>
    /// An MSBuild task that acts as a shim to <c>Microsoft.AspNetCore.BuildTools.ApiCheck.exe compare ...</c> or
    /// <c>dotnet Microsoft.AspNetCore.BuildTools.ApiCheck.dll compare ...</c>.
    /// </summary>
    public class ApiCheckTask : ApiCheckTasksBase
    {
        public ApiCheckTask()
        {
            // Tool does not use stderr for anything. Treat everything that appears there as an error.
            LogStandardErrorAsError = true;
        }

        /// <summary>
        /// Path to the API listing file to use as reference.
        /// </summary>
        [Required]
        public string ApiListingPath { get; set; }

        /// <summary>
        /// Exclude types defined in .Internal namespaces from the comparison, ignoring breaking changes in such types.
        /// </summary>
        public bool ExcludePublicInternalTypes { get; set; }

        /// <summary>
        /// Path to the exclusions file that narrows <see cref="ApiListingPath"/>, ignoring listed breaking changes.
        /// </summary>
        public string ExclusionsPath { get; set; }

        /// <summary>
        /// Path to the project.assets.json file created when building <see cref="AssemblyPath"/>.
        /// </summary>
        [Required]
        public string ProjetAssetsPath { get; set; }

        /// <inheritdoc />
        protected override MessageImportance StandardErrorLoggingImportance => MessageImportance.High;

        /// <inheritdoc />
        protected override MessageImportance StandardOutputLoggingImportance => MessageImportance.High;


        /// <inheritdoc />
        protected override bool ValidateParameters()
        {
            if (string.IsNullOrEmpty(ApiListingPath) || !File.Exists(ApiListingPath))
            {
                Log.LogError($"API listing file '{ApiListingPath}' not specified or does not exist.");
                return false;
            }

            if (string.IsNullOrEmpty(AssemblyPath) || !File.Exists(AssemblyPath))
            {
                Log.LogError($"Assembly '{AssemblyPath}' not specified or does not exist.");
                return false;
            }

            if (string.IsNullOrEmpty(Framework))
            {
                Log.LogError("Framework moniker must be specified.");
                return false;
            }

            if (string.IsNullOrEmpty(ProjetAssetsPath) || !File.Exists(ProjetAssetsPath))
            {
                Log.LogError($"Project assets file '{ProjetAssetsPath}' not specified or does not exist.");
                return false;
            }

            if (!string.IsNullOrEmpty(ExclusionsPath) && !File.Exists(ExclusionsPath))
            {
                Log.LogError($"Exclusions file '{ExclusionsPath}' does not exist.");
                return false;
            }

            return base.ValidateParameters();
        }

        /// <inheritdoc />
        protected override string GenerateCommandLineCommands()
        {
            var arguments = string.Empty;
            if (!Framework.StartsWith("net4", StringComparison.OrdinalIgnoreCase))
            {
                var taskAssemblyFolder = Path.GetDirectoryName(GetType().GetTypeInfo().Assembly.Location);
                var toolPath = Path.Combine(taskAssemblyFolder, "..", "netcoreapp2.0", ApiCheckToolName + ".dll");
                arguments = $@"""{Path.GetFullPath(toolPath)}"" ";
            }

            arguments += "compare";
            if (ExcludePublicInternalTypes)
            {
                arguments += " --exclude-public-internal";
            }

            arguments += $@" --assembly ""{AssemblyPath}"" --framework {Framework}";
            arguments += $@" --project ""{ProjetAssetsPath}"" --api-listing ""{ApiListingPath}""";
            if (!string.IsNullOrEmpty(ExclusionsPath))
            {
                arguments += $@" --exclusions ""{ExclusionsPath}""";
            }

            return arguments;
        }
    }
}
