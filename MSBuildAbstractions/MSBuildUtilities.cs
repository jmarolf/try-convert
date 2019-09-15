﻿using Facts;
using Microsoft.Build.Construction;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;

namespace ProjectSimplifier
{
    public static class MSBuildUtilities
    {
        /// <summary>
        /// matches $(name) pattern
        /// </summary>
        private static readonly Regex DimensionNameInConditionRegex = new Regex(@"^\$\(([^\$\(\)]*)\)$");

        /// <summary>
        /// Converts configuration dimensional value vector to a msbuild condition
        /// Use the standard format of
        /// '$(DimensionName1)|$(DimensionName2)|...|$(DimensionNameN)'=='DimensionValue1|...|DimensionValueN'
        /// </summary>
        /// <param name="dimensionalValues">vector of configuration dimensional properties</param>
        /// <returns>msbuild condition representation</returns>
        internal static string DimensionalValuePairsToCondition(ImmutableDictionary<string, string> dimensionalValues)
        {
            if (null == dimensionalValues || 0 == dimensionalValues.Count)
            {
                return string.Empty; // no condition. Returns empty string to match MSBuild.
            }

            string left = string.Empty;
            string right = string.Empty;

            foreach (string key in dimensionalValues.Keys)
            {
                if (!string.IsNullOrEmpty(left))
                {
                    left += "|";
                    right += "|";
                }

                left += "$(" + key + ")";
                right += dimensionalValues[key];
            }

            string condition = "'" + left + "'=='" + right + "'";
            return condition;
        }

        /// <summary>
        /// Returns a name of a configuration like Debug|AnyCPU
        /// </summary>
        public static string GetConfigurationName(ImmutableDictionary<string, string> dimensionValues) => dimensionValues.IsEmpty ? "" : dimensionValues.Values.Aggregate((x, y) => $"{x}|{y}");

        /// <summary>
        /// Returns a name of a configuration like Debug|AnyCPU
        /// </summary>
        public static string GetConfigurationName(string condition)
        {
            if (ConditionToDimensionValues(condition, out var dimensionValues))
            {
                return GetConfigurationName(dimensionValues);
            }

            return "";
        }

        /// <summary>
        /// Tries to parse an MSBuild condition to a dimensional vector
        /// only matches standard pattern:
        /// '$(DimensionName1)|$(DimensionName2)|...|$(DimensionNameN)'=='DimensionValue1|...|DimensionValueN'
        /// </summary>
        /// <param name="condition">msbuild condition string</param>
        /// <param name="dimensionalValues">configuration dimensions vector (output)</param>
        /// <returns>true on success</returns>
        public static bool ConditionToDimensionValues(string condition, out ImmutableDictionary<string, string> dimensionalValues)
        {
            string left;
            string right;
            dimensionalValues = ImmutableDictionary<string, string>.Empty;

            if (string.IsNullOrEmpty(condition))
            {
                // yes empty condition is recognized as a empty dimension vector
                return true;
            }

            int equalPos = condition.IndexOf("==", StringComparison.OrdinalIgnoreCase);
            if (equalPos <= 0)
            {
                return false;
            }

            left = condition.Substring(0, equalPos).Trim();
            right = condition.Substring(equalPos + 2).Trim();

            // left and right needs to ba a valid quoted strings
            if (!UnquoteString(ref left) || !UnquoteString(ref right))
            {
                return false;
            }

            string[] dimensionNamesInCondition = left.Split(new char[] { '|' });
            string[] dimensionValuesInCondition = right.Split(new char[] { '|' });

            // number of keys need to match number of values
            if (dimensionNamesInCondition.Length == 0 || dimensionNamesInCondition.Length != dimensionValuesInCondition.Length)
            {
                return false;
            }

            Dictionary<string, string> parsedDimensionalValues = new Dictionary<string, string>(dimensionNamesInCondition.Length);

            for (int i = 0; i < dimensionNamesInCondition.Length; i++)
            {
                // matches "$(name)" patern.
                Match match = DimensionNameInConditionRegex.Match(dimensionNamesInCondition[i]);
                if (!match.Success)
                {
                    return false;
                }

                string dimensionName = match.Groups[1].ToString();
                if (string.IsNullOrEmpty(dimensionName))
                {
                    return false;
                }

                parsedDimensionalValues[dimensionName] = dimensionValuesInCondition[i];
            }

            dimensionalValues = parsedDimensionalValues.ToImmutableDictionary();
            return true;
        }

        public static bool FrameworkHasAValueTuple(string tfm)
        {
            if (tfm is null
                || tfm.ContainsIgnoreCase(MSBuildFacts.NetstandardPrelude)
                || tfm.ContainsIgnoreCase(MSBuildFacts.NetcoreappPrelude))
            {
                return false;
            }

            if (!tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return tfm.StartsWith(Facts.MSBuildFacts.LowestFrameworkVersionWithSystemValueTuple);
        }

        public static bool IsPackageReference(ProjectItemElement element) => element.ElementName.Equals(PackageFacts.PackageReferenceItemType, StringComparison.OrdinalIgnoreCase);

        public static IEnumerable<ProjectItemElement> GetCandidateItemsForRemoval(ProjectItemGroupElement itemGroup) =>
            itemGroup.Items.Where(item => item.ElementName.Equals(Facts.MSBuildFacts.MSBuildReferenceName, StringComparison.OrdinalIgnoreCase)
                                          || Facts.MSBuildFacts.GlobbedItemTypes.Contains(item.ElementName, StringComparer.OrdinalIgnoreCase));

        public static IEnumerable<ProjectItemElement> GetReferences(ProjectItemGroupElement itemGroup) =>
            itemGroup.Items.Where(item => item.ElementName.Equals(Facts.MSBuildFacts.MSBuildReferenceName, StringComparison.OrdinalIgnoreCase));

        public static bool IsWPF(IProjectRootElement projectRoot)
        {
            var references = projectRoot.ItemGroups.SelectMany(GetReferences)?.Select(elem => elem.Include);
            return DesktopFacts.KnownWPFReferences.All(reference => references.Contains(reference, StringComparer.OrdinalIgnoreCase));
        }

        public static bool IsWinForms(IProjectRootElement projectRoot)
        {
            var references = projectRoot.ItemGroups.SelectMany(GetReferences)?.Select(elem => elem.Include);
            return DesktopFacts.KnownWinFormsReferences.All(reference => references.Contains(reference, StringComparer.OrdinalIgnoreCase));
        }

        public static bool IsNotNetFramework(string tfm) => 
            !tfm.ContainsIgnoreCase(MSBuildFacts.NetcoreappPrelude)
            && !tfm.ContainsIgnoreCase(MSBuildFacts.NetstandardPrelude);

        /// <summary>
        /// Checks if a given item needs to be removed because it either only runs on desktop .NET or is automatically pulled in as a reference and is thus unnecessary.
        /// </summary>
        public static bool DesktopReferencesNeedsRemoval(ProjectItemElement item) =>
            MSBuildFacts.ItemsWithPackagesThatWorkOnNETCore.Contains(item.Include, StringComparer.OrdinalIgnoreCase)
            || DesktopFacts.ReferencesThatNeedRemoval.Contains(item.Include, StringComparer.OrdinalIgnoreCase)
            || DesktopFacts.KnownWPFReferences.Contains(item.Include, StringComparer.OrdinalIgnoreCase)
            || DesktopFacts.KnownWinFormsReferences.Contains(item.Include, StringComparer.OrdinalIgnoreCase);

        public static bool IsExplicitValueTupleReferenceNeeded(string tfm) => FrameworkHasAValueTuple(tfm);

        public static bool IsExplicitValueTupleReferenceNeeded(ProjectItemElement item, string tfm) =>
            item.Include.Equals(Facts.MSBuildFacts.SystemValueTupleName, StringComparison.OrdinalIgnoreCase) && FrameworkHasAValueTuple(tfm);

        /// <summary>
        /// Checks if the given item is a designer file that is not one of { Settings.Designer.cs, Resources.Designer.cs }.
        /// </summary>
        /// <param name="item">The ProjectItemElement that might be a designer file.</param>
        /// <returns>true if the given ProjectItemElement is a designer file that isn't a settings or resources file.</returns>
        public static bool IsWinFormsUIDesignerFile(ProjectItemElement item) =>
            item.Include.EndsWith(DesktopFacts.DesignerEndString, StringComparison.OrdinalIgnoreCase)
            && !item.Include.EndsWith(DesktopFacts.ResourcesDesignerFileName, StringComparison.OrdinalIgnoreCase)
            && !item.Include.EndsWith(DesktopFacts.SettingsDesignerFileName, StringComparison.OrdinalIgnoreCase);

        public static bool IsDefineConstantDefault(ProjectPropertyElement prop) =>
            prop.ElementName.Equals(MSBuildFacts.DefineConstantsName, StringComparison.OrdinalIgnoreCase)
            && prop.Value.Split(';').All(constant => MSBuildFacts.DefaultDefineConstants.Contains(constant, StringComparer.OrdinalIgnoreCase));

        public static bool IsDebugTypeDefault(ProjectPropertyElement prop) =>
            prop.ElementName.Equals(MSBuildFacts.DebugTypeName, StringComparison.OrdinalIgnoreCase)
            && Facts.MSBuildFacts.DefaultDebugTypes.Contains(prop.Value, StringComparer.OrdinalIgnoreCase);

        public static bool IsOutputPathDefault(ProjectPropertyElement prop) =>
            prop.ElementName.Equals(MSBuildFacts.OutputPathName, StringComparison.OrdinalIgnoreCase)
            && Facts.MSBuildFacts.DefaultOutputPaths.Contains(prop.Value, StringComparer.OrdinalIgnoreCase);

        public static bool IsPlatformTargetDefault(ProjectPropertyElement prop) =>
            prop.ElementName.Equals(MSBuildFacts.PlatformTargetName, StringComparison.OrdinalIgnoreCase)
            && Facts.MSBuildFacts.DefaultPlatformTargets.Contains(prop.Value, StringComparer.OrdinalIgnoreCase);

        public static bool IsLegacyXamlDesignerItem(ProjectItemElement item) =>
            item.Include.EndsWith(DesktopFacts.XamlFileExtension, StringComparison.OrdinalIgnoreCase)
            && item.Metadata.Any(pme => pme.Name.Equals(MSBuildFacts.SubTypeName, StringComparison.OrdinalIgnoreCase)
                                       && pme.Value.Equals(MSBuildFacts.DesignerSubType, StringComparison.OrdinalIgnoreCase));

        public static bool IsDependentUponXamlDesignerItem(ProjectItemElement item) =>
            item.Metadata.Any(pme => pme.Name.Equals(MSBuildFacts.SubTypeName, StringComparison.OrdinalIgnoreCase)
                                     && pme.Value.Equals(MSBuildFacts.CodeSubType, StringComparison.OrdinalIgnoreCase))
            && item.Metadata.Any(pme => pme.Name.Equals(MSBuildFacts.DependentUponName, StringComparison.OrdinalIgnoreCase)
                                        && pme.Value.EndsWith(DesktopFacts.XamlFileExtension, StringComparison.OrdinalIgnoreCase));

        public static IEnumerable<ProjectItemGroupElement> GetPackagesConfigItemGroup(IProjectRootElement root) =>
            root.ItemGroups.Where(pige => pige.Items.Any(pe => pe.Include.Equals(PackageFacts.PackagesConfigIncludeName, StringComparison.OrdinalIgnoreCase)));

        public static ProjectItemElement GetPackagesConfigItem(ProjectItemGroupElement packagesConfigItemGroup) =>
            packagesConfigItemGroup.Items.Single(pe => pe.Include.Equals(PackageFacts.PackagesConfigIncludeName, StringComparison.OrdinalIgnoreCase));

        public static void AddUseWinForms(ProjectPropertyGroupElement propGroup) => propGroup.AddProperty(DesktopFacts.UseWinFormsPropertyName, "true");
        public static void AddUseWPF(ProjectPropertyGroupElement propGroup) => propGroup.AddProperty(DesktopFacts.UseWPFPropertyName, "true");

        public static ProjectPropertyGroupElement GetTopPropertyGroupWithTFM(IProjectRootElement rootElement) =>
            rootElement.PropertyGroups.Single(pg => pg.Children.Any(p => p.ElementName == MSBuildFacts.TargetFrameworkNodeName));

        public static bool IsValidMetadataForConversionPurposes(IProjectMetadata projectMetadata) =>
            !projectMetadata.Name.Equals(MSBuildFacts.RequiredTargetFrameworkNodeName, StringComparison.OrdinalIgnoreCase);


        /// <summary>
        /// Unquote string. It simply removes the starting and ending "'", and checks they are present before.
        /// </summary>
        /// <param name="s">string to unquote </param>
        /// <returns>true if string is successfuly unquoted</returns>
        private static bool UnquoteString(ref string s)
        {
            if (s.Length < 2 || s[0] != '\'' || s[s.Length - 1] != '\'')
            {
                return false;
            }

            s = s.Substring(1, s.Length - 2);
            return true;
        }
    }
}