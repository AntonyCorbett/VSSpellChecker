﻿//===============================================================================================================
// System  : Visual Studio Spell Checker Package
// File    : SpellCheckFileInfo.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 09/04/2022
// Note    : Copyright 2015-2022, Eric Woodruff, All rights reserved
//
// This file contains a class used to hold information about a file that will be spell checked
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://github.com/EWSoftware/VSSpellChecker
// This notice, the author's name, and all copyright notices must remain intact in all applications,
// documentation, and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 08/26/2015  EFW  Created the code
//===============================================================================================================

// Ignore spelling: proj

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using EnvDTE;
using EnvDTE80;

using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;

using VisualStudio.SpellChecker.Configuration;

namespace VisualStudio.SpellChecker.ProjectSpellCheck
{
    internal class SpellCheckFileInfo
    {
        #region Private data members
        //=====================================================================

        private static readonly SpellCheckFileInfo IgnoredHierarchyItem = new SpellCheckFileInfo();
        private static readonly char[] validChars = new[] { '\b', '\t', '\r', '\n', '\x07', '\x0B', '\x0C' };

        #endregion

        #region Properties
        //=====================================================================

        /// <summary>
        /// This read-only property returns the containing solution filename
        /// </summary>
        public string SolutionFile { get; private set; }

        /// <summary>
        /// This read-only property returns the containing project filename
        /// </summary>
        public string ProjectFile { get; private set; }

        /// <summary>
        /// This read-only property returns the filename (no path)
        /// </summary>
        public string Filename { get; private set; }

        /// <summary>
        /// This read-only property returns the file's canonical name (full path)
        /// </summary>
        public string CanonicalName { get; private set; }

        /// <summary>
        /// This read-only property returns the potential name of the solution configuration file
        /// </summary>
        public string SolutionConfigurationFile { get; private set; }

        /// <summary>
        /// This read-only property returns the potential name of the project configuration file
        /// </summary>
        public string ProjectConfigurationFile { get; private set; }

        /// <summary>
        /// This read-only property returns the potential name of the related configuration file for the spell
        /// checked file.
        /// </summary>
        public string RelatedConfigurationFile { get; private set; }

        /// <summary>
        /// This read-only property returns the name of a dependency configuration file if there is one or null
        /// if there is not.
        /// </summary>
        public string DependencyConfigurationFile { get; private set; }

        /// <summary>
        /// This read-only property returns true if this item represents a code analysis dictionary, false if not
        /// </summary>
        public bool IsCodeAnalysisDictionary { get; private set; }

        /// <summary>
        /// This returns a description of the item with the solution and relative path to the file
        /// </summary>
        public string Description
        {
            get
            {
                string projectPath = Path.GetDirectoryName(this.ProjectFile), filePath = Path.GetDirectoryName(this.CanonicalName);

                if(projectPath.Length == 0 || !filePath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
                {
                    projectPath = Path.GetDirectoryName(this.SolutionFile ?? ".");

                    if(projectPath.Length == 0 || !filePath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
                        return Path.GetFileName(this.ProjectFile) + " / " + this.Filename;

                    return Path.GetFileName(this.ProjectFile) + " / " + this.CanonicalName.Substring(projectPath.Length + 1);
                }

                return Path.GetFileName(this.ProjectFile) + " / " + this.CanonicalName.Substring(projectPath.Length + 1);
            }
        }

        /// <summary>
        /// This read-only property returns any folder configuration files that should be used
        /// </summary>
        /// <value>The configuration files are in order from parent to child folders</value>
        public IEnumerable<string> FolderConfigurationFiles { get; private set; }

        /// <summary>
        /// This read-only property returns an enumerable list of ignored words files that were loaded by the
        /// configuration.
        /// </summary>
        public IEnumerable<(ConfigurationType ConfigType, string Filename)> IgnoredWordsFiles { get; private set; }

        #endregion

        #region Helper methods
        //=====================================================================

        /// <summary>
        /// Return spell check file info for an open document
        /// </summary>
        /// <param name="filename">The filename of the open document</param>
        /// <returns>An instance for an open document</returns>
        public static SpellCheckFileInfo ForOpenDocument(string filename)
        {
            return new SpellCheckFileInfo
            {
                ProjectFile = "Open Document",
                Filename = Path.GetFileName(filename),
                CanonicalName = filename
            };
        }

        /// <summary>
        /// This is used to get the code analysis dictionaries from the named project
        /// </summary>
        /// <param name="projectName">The project from which to get code analysis dictionaries</param>
        /// <returns>An enumerable list of the code analysis dictionary files if any</returns>
        public static IEnumerable<SpellCheckFileInfo> ProjectCodeAnalysisDictionaries(string projectName)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();

            List<SpellCheckFileInfo> projectFiles = new List<SpellCheckFileInfo>();

            try
            {
                var solution = Utility.GetServiceFromPackage<IVsSolution, IVsSolution>(false);

                if(solution != null)
                {
                    // Use the IVsHierarchy interface as it is reportedly significantly faster than using the
                    // automation interfaces for very large projects.
                    var hierarchy = (IVsHierarchy)solution;

                    if((projectName == null || solution.GetProjectOfUniqueName(projectName,
                        out hierarchy) == VSConstants.S_OK) && hierarchy != null)
                    {
                        ProcessHierarchyNodeRecursively(hierarchy, VSConstants.VSITEMID_ROOT, projectFiles);
                    }
                }
            }
            catch(Exception ex)
            {
                // Ignore exceptions, just return what we could get
                System.Diagnostics.Debug.WriteLine(ex);
            }

            return projectFiles.Where(f => f.IsCodeAnalysisDictionary);
        }

        /// <summary>
        /// This is used to get information for all files in the solution or a specific project
        /// </summary>
        /// <param name="projectName">The project filename from which to get the file information or null to
        /// return information for all files in all projects in the solution</param>
        /// <returns>An enumerable list of project file information</returns>
        public static IEnumerable<SpellCheckFileInfo> AllProjectFiles(string projectName)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();

            List<SpellCheckFileInfo> projectFiles = new List<SpellCheckFileInfo>();

            try
            {
                var solution = Utility.GetServiceFromPackage<IVsSolution, IVsSolution>(false);

                if(solution != null && solution.GetSolutionInfo(out _, out string solutionFile, out _) == VSConstants.S_OK)
                {
                    string solutionConfigFile = solutionFile + ".vsspell";

                    // Allow for solution configuration files to be named ".vsspell"
                    if(!File.Exists(solutionConfigFile))
                        solutionConfigFile = Path.Combine(Path.GetDirectoryName(solutionFile), ".vsspell");

                    // Use the IVsHierarchy interface as it is reportedly significantly faster than using the
                    // automation interfaces for very large projects.
                    var hierarchy = (IVsHierarchy)solution;

                    if(projectName != null)
                    {
                        // See if there is a solution configuration.  We need to look for it separately as
                        // we won't see it when getting the files for a single project.
                        var dte2 = Utility.GetServiceFromPackage<DTE2, SDTE>(false);

                        if(dte2 != null)
                        {
                            var projectItem = dte2.Solution.FindProjectItemForFile(solutionConfigFile);

                            if(projectItem != null)
                                projectFiles.Add(new SpellCheckFileInfo
                                {
                                    ProjectFile = "Solution Items",
                                    Filename = Path.GetFileName(solutionConfigFile),
                                    CanonicalName = solutionConfigFile
                                });
                        }

                        if(solution.GetProjectOfUniqueName(projectName, out hierarchy) != VSConstants.S_OK)
                            hierarchy = null;
                    }

                    if(hierarchy != null)
                    {
                        ProcessHierarchyNodeRecursively(hierarchy, VSConstants.VSITEMID_ROOT, projectFiles);

                        // Remove spell checker configuration files
                        var configFiles = projectFiles.Where(p => p.Filename.EndsWith(".vsspell",
                            StringComparison.OrdinalIgnoreCase)).ToList();

                        projectFiles = projectFiles.Except(configFiles).OrderBy(
                            p => Path.GetFileName(p.ProjectFile)).ThenBy(p => p.Filename).ToList();

                        // Determine folder configuration files
                        var configNames = new HashSet<string>(configFiles.Select(p => p.CanonicalName),
                            StringComparer.OrdinalIgnoreCase);
                        List<(string ConfigPath, string ConfigFile)> folderConfigFiles = configNames.Where(f =>
                        {
                            int pos = f.LastIndexOf('\\');

                            if(pos > 0)
                            {
                                // If the subfolder name preceding the filename matches the filename without the
                                // .vsspell extension, it's a folder configuration file.
                                int subFolderPos = f.LastIndexOf('\\', pos - 1);
                                return subFolderPos != -1 && String.Compare(f, subFolderPos, f, pos,
                                    pos - subFolderPos, StringComparison.OrdinalIgnoreCase) == 0 &&
                                    f.Length - 8 == pos + (pos - subFolderPos);
                            }

                            return false;

                        }).Select(f => (f.Substring(0, f.LastIndexOf('\\') + 1), f)).OrderBy(f => f.Item1).ToList();

                        // Set the solution and configuration file info for each project file
                        foreach(var file in projectFiles)
                        {
                            file.SolutionFile = solutionFile;
                            file.SolutionConfigurationFile = solutionConfigFile;
                            file.ProjectConfigurationFile = file.ProjectFile + ".vsspell";
                            file.RelatedConfigurationFile = file.CanonicalName + ".vsspell";

                            var folderConfigs = new List<string>();

                            file.FolderConfigurationFiles = folderConfigs;

                            // Dependency configurations are a bit of a problem as we don't know for sure if
                            // this is the dependency of another here.  We'll assume that if there's a
                            // configuration for a file that starts with its name but not for this one, it's
                            // the parent's configuration file and use it.  Should be true for most cases.
                            int idx = file.CanonicalName.LastIndexOf('\\');

                            if(idx != -1)
                            {
                                // Look for the first period, not the last, as we want to look for parent
                                // filenames (i.e. Form.cs for Form.Designer.cs).  Add one to the index as
                                // we don't want to match one for another file such as Form2.cs.
                                int extIdx = file.CanonicalName.IndexOf('.', idx) + 1;

                                // Ignore it if not found or the filename starts with a period like .editorconfig
                                if(extIdx > idx + 2 && extIdx < file.CanonicalName.Length)
                                {
                                    file.DependencyConfigurationFile = configNames.FirstOrDefault(n =>
                                        String.Compare(n, 0, file.CanonicalName, 0, extIdx,
                                            StringComparison.OrdinalIgnoreCase) == 0 &&
                                        !n.StartsWith(solutionFile, StringComparison.OrdinalIgnoreCase) &&
                                        !n.StartsWith(file.ProjectFile, StringComparison.OrdinalIgnoreCase) &&
                                        !n.StartsWith(file.CanonicalName, StringComparison.OrdinalIgnoreCase));
                                }
                            }

                            // Add any folder-specific configuration files that apply
                            if(folderConfigFiles.Count != 0)
                            {
                                folderConfigs.AddRange(folderConfigFiles.Where(f =>
                                    file.CanonicalName.StartsWith(f.ConfigPath,
                                    StringComparison.OrdinalIgnoreCase)).Select(f => f.ConfigFile));
                            }
                        }
                    }
                }
            }
            catch(Exception ex)
            {
                // Ignore exceptions, just return what we could get
                System.Diagnostics.Debug.WriteLine(ex);
            }

            return projectFiles;
        }

        /// <summary>
        /// This is used to get information for only the files related to the currently selected items in the
        /// Solution Explorer.
        /// </summary>
        /// <returns>An enumerable list of all selected project file information.  If the solution node is
        /// selected, all files are returned.  If a project is selected, all files in the project are returned.
        /// If a folder is selected, all files in the folder are returned.  If a file is selected that has
        /// dependency items, those are returned as well.</returns>
        public static IEnumerable<SpellCheckFileInfo> SelectedProjectFiles()
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();

#pragma warning disable VSTHRD010
            List<SpellCheckFileInfo> projectFiles = new List<SpellCheckFileInfo>();
            var dte2 = Utility.GetServiceFromPackage<DTE2, SDTE>(false);

            if(dte2 == null)
                return projectFiles;

            List<string> projects = new List<string>(), folders = new List<string>(), files = new List<string>();
            bool entireSolution = false;

            // This is a bit complicated but we need to figure out which items are selected and then filter down
            // the entire set based on what was selected.
            foreach(SelectedItem item in dte2.SelectedItems)
            {
                // For vsProjectKindSolutionItems, enumerate projects first if there are any and then fall
                // through to handle solution items.
                if(item.Project != null && item.Project.Kind == EnvDTE.Constants.vsProjectKindSolutionItems)
                    projects.AddRange(item.Project.EnumerateProjects().Select(p => p.FullName));

                if(item.Project != null && item.Project.Kind != EnvDTE.Constants.vsProjectKindSolutionItems &&
                  item.Project.Kind != EnvDTE.Constants.vsProjectKindUnmodeled &&
                  item.Project.Kind != EnvDTE.Constants.vsProjectKindMisc)
                {
                    string path = null;

                    // Looks like a project.  Not all of them implement properties though.
                    if(!String.IsNullOrWhiteSpace(item.Project.FullName) && item.Project.FullName.EndsWith(
                      "proj", StringComparison.OrdinalIgnoreCase))
                    {
                        path = item.Project.FullName;
                    }

                    if(path == null && item.Project.Properties != null)
                    {
                        Property fullPath;

                        try
                        {
                            fullPath = item.Project.Properties.Item("FullPath");
                        }
                        catch
                        {
                            // C++ projects use a different property name and throw an exception above
                            try
                            {
                                fullPath = item.Project.Properties.Item("ProjectFile");
                            }
                            catch
                            {
                                // If that fails, give up
                                fullPath = null;
                            }
                        }

                        if(fullPath != null && fullPath.Value != null)
                            path = (string)fullPath.Value;
                    }

                    if(!String.IsNullOrWhiteSpace(path))
                    {
                        var project = dte2.Solution.EnumerateProjects().FirstOrDefault(p => p.Name == item.Name);

                        if(project != null)
                            projects.Add(project.FullName);
                    }
                }
                else
                    if(item.ProjectItem == null || item.ProjectItem.ContainingProject == null)
                    {
                        // Looks like a solution or a solution items folder
                        if(Path.GetFileNameWithoutExtension(dte2.Solution.FullName) == item.Name)
                        {
                            entireSolution = true;
                            break;
                        }

                        string folderName = Path.Combine(Path.GetDirectoryName(dte2.Solution.FullName), item.Name);

                        // If the folder exists, it's a folder.  If not, it's probably the Solution Items
                        // container node.  However, ignore the References container node.
                        if(Directory.Exists(folderName))
                            folders.Add(folderName + "\\");
                        else
                            if(item.Name != "References")
                                projects.Add("Solution Items");
                    }
                    else
                        if(item.ProjectItem.Properties != null)
                        {
                            // Looks like a folder or file item
                            Property fullPath = null;

                            if(item.ProjectItem.Kind != EnvDTE.Constants.vsProjectItemKindVirtualFolder)
                                fullPath = item.ProjectItem.Properties.Item("FullPath");

                            if(fullPath != null && fullPath.Value != null)
                            {
                                string path = (string)fullPath.Value;

                                if(!String.IsNullOrWhiteSpace(path))
                                {
                                    // Folder items have a trailing backslash in some project systems, others don't
                                    if(path[path.Length - 1] == '\\' || (!File.Exists(path) && Directory.Exists(path)))
                                    {
                                        if(path[path.Length - 1] != '\\')
                                            path += @"\";

                                        folders.Add(path);
                                    }
                                    else
                                    {
                                        files.Add(path);

                                        // If the file has dependency items, add them too
                                        if(item.ProjectItem.ProjectItems != null &&
                                          item.ProjectItem.ProjectItems.Count != 0)
                                            foreach(ProjectItem dep in item.ProjectItem.ProjectItems)
                                            {
                                                files.Add(dep.get_FileNames(1));
                                            }
                                    }
                                }
                            }
                        }
                        else
                            if(item.ProjectItem.Kind == EnvDTE.Constants.vsProjectItemKindSolutionItems)
                            {
                                // Looks like a solution item file
                                files.Add(item.ProjectItem.get_FileNames(1));
                            }
            }
#pragma warning restore VSTHRD010

            var allFiles = AllProjectFiles(null);

            if(entireSolution)
                return allFiles;

            HashSet<string> filenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach(string projectName in projects)
            {
                var pf = allFiles.Where(f => !filenames.Contains(f.CanonicalName) &&
                    f.ProjectFile.Equals(projectName, StringComparison.OrdinalIgnoreCase)).ToList();

                filenames.UnionWith(pf.Select(f => f.CanonicalName));

                projectFiles.AddRange(pf);
            }

            foreach(string folderName in folders)
            {
                var pf = allFiles.Where(f => !filenames.Contains(f.CanonicalName) &&
                    f.CanonicalName.StartsWith(folderName, StringComparison.OrdinalIgnoreCase)).ToList();

                filenames.UnionWith(pf.Select(f => f.CanonicalName));

                projectFiles.AddRange(pf);
            }

            foreach(string fileName in files)
            {
                var pf = allFiles.Where(f => !filenames.Contains(f.CanonicalName) &&
                    f.CanonicalName.Equals(fileName, StringComparison.OrdinalIgnoreCase)).ToList();

                filenames.UnionWith(pf.Select(f => f.CanonicalName));

                projectFiles.AddRange(pf);
            }

            return projectFiles;
        }

        /// <summary>
        /// Process all project hierarchy nodes recursively returning information about the files in them
        /// </summary>
        /// <param name="hierarchy">The starting hierarchy node</param>
        /// <param name="itemId">The item ID</param>
        /// <param name="projectFiles">The list to which project file information is added</param>
        private static void ProcessHierarchyNodeRecursively(IVsHierarchy hierarchy, uint itemId,
          IList<SpellCheckFileInfo> projectFiles)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            IVsHierarchy nestedHierarchy;

            // First, guess if the node is actually the root of another hierarchy (a project, for example)
            Guid nestedHierarchyGuid = typeof(IVsHierarchy).GUID;
            int result = hierarchy.GetNestedHierarchy(itemId, ref nestedHierarchyGuid, out IntPtr nestedHierarchyValue,
                out uint nestedItemIdValue);

            if(result == VSConstants.S_OK && nestedHierarchyValue != IntPtr.Zero && nestedItemIdValue == VSConstants.VSITEMID_ROOT)
            {
                // Get the new hierarchy
                nestedHierarchy = System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(
                    nestedHierarchyValue) as IVsHierarchy;
                System.Runtime.InteropServices.Marshal.Release(nestedHierarchyValue);

                if(nestedHierarchy != null)
                    ProcessHierarchyNodeRecursively(nestedHierarchy, VSConstants.VSITEMID_ROOT, projectFiles);
            }
            else
            {
                // The node is not the root of another hierarchy, it is a regular node
                var projectFile = DetermineProjectFileInformation(hierarchy, itemId);

                if(projectFile != IgnoredHierarchyItem)
                {
                    if(projectFile != null)
                        projectFiles.Add(projectFile);

                    result = hierarchy.GetProperty(itemId, (int)__VSHPROPID.VSHPROPID_FirstVisibleChild,
                        out object value);

                    while(result == VSConstants.S_OK && value != null && value is int nodeId)
                    {
                        uint visibleChildNode = (uint)nodeId;

                        if(visibleChildNode == VSConstants.VSITEMID_NIL)
                            break;

                        ProcessHierarchyNodeRecursively(hierarchy, visibleChildNode, projectFiles);

                        result = hierarchy.GetProperty(visibleChildNode, (int)__VSHPROPID.VSHPROPID_NextVisibleSibling,
                            out value);
                    }
                }
            }
        }

        /// <summary>
        /// This is used to determine project and file information for a hierarchy node
        /// </summary>
        /// <param name="hierarchy">The hierarchy node to examine</param>
        /// <param name="itemId">The item ID</param>
        /// <remarks>This filters out the root solution node, project nodes, folder nodes, and any other
        /// unrecognized nodes.</remarks>
        private static SpellCheckFileInfo DetermineProjectFileInformation(IVsHierarchy hierarchy, uint itemId)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();

            if(hierarchy is IVsProject project)
            {
                int result = project.GetMkDocument(VSConstants.VSITEMID_ROOT, out string projectName);

                // If there is no project name, it's probably a solution item
                if(result != VSConstants.S_OK)
                    projectName = "Solution Items";
                else
                    if(projectName.Length > 1 && projectName[projectName.Length - 1] == '\\')
                        projectName += Path.GetFileName(projectName.Substring(0, projectName.Length - 1));

                result = hierarchy.GetProperty(itemId, (int)__VSHPROPID.VSHPROPID_Name, out object value);

                if(result == VSConstants.S_OK && value != null)
                {
                    string name = value.ToString();

                    // Certain project folders in C++ projects return a GUID for their name.  These should be
                    // ignored (References, External Dependencies, etc.).
                    if(name.Length != 0 && name[0] == '{' && Guid.TryParse(name, out _))
                        return IgnoredHierarchyItem;

                    result = hierarchy.GetCanonicalName(itemId, out string canonicalName);

                    if(result == VSConstants.S_OK && !String.IsNullOrWhiteSpace(canonicalName) &&
                      canonicalName.IndexOfAny(Path.GetInvalidPathChars()) == -1 &&
                      Path.IsPathRooted(canonicalName) && !canonicalName.EndsWith("\\", StringComparison.Ordinal) &&
                      !canonicalName.Equals(projectName, StringComparison.OrdinalIgnoreCase))
                    {
                        result = hierarchy.GetProperty(itemId, (int)__VSHPROPID4.VSHPROPID_BuildAction, out value);

                        bool isCodeAnalysisDictionary = (result == VSConstants.S_OK && value != null &&
                            ((string)value).Equals("CodeAnalysisDictionary", StringComparison.OrdinalIgnoreCase));

                        return new SpellCheckFileInfo
                        {
                            ProjectFile = projectName,
                            Filename = name,
                            CanonicalName = canonicalName,
                            IsCodeAnalysisDictionary = isCodeAnalysisDictionary
                        };
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// This read-only property returns an enumerable list of the configuration files that will be loaded to
        /// create the configuration used to spell check the file.
        /// </summary>
        /// <returns>An enumerable list of tuples containing the configuration file type and the configuration
        /// filename.</returns>
        public IEnumerable<(ConfigurationType ConfigType, string ConfigFile)> ConfigurationFiles
        {
            get
            {
                // Start with the global configuration and work down
                yield return (ConfigurationType.Global, SpellingConfigurationFile.GlobalConfigurationFilename);

                if(this.SolutionConfigurationFile != null && File.Exists(this.SolutionConfigurationFile))
                    yield return (ConfigurationType.Solution, this.SolutionConfigurationFile);

                if(this.ProjectConfigurationFile != null && File.Exists(this.ProjectConfigurationFile))
                    yield return (ConfigurationType.Project, this.ProjectConfigurationFile);

                foreach(string cf in this.FolderConfigurationFiles.Where(c => File.Exists(c)))
                    yield return (ConfigurationType.Folder, cf);

                if(this.DependencyConfigurationFile != null && File.Exists(this.DependencyConfigurationFile))
                    yield return (ConfigurationType.File, this.DependencyConfigurationFile);

                if(this.RelatedConfigurationFile != null && File.Exists(this.RelatedConfigurationFile))
                    yield return (ConfigurationType.File, this.RelatedConfigurationFile);
            }
        }

        /// <summary>
        /// This is used to generate the configuration for the instance
        /// </summary>
        /// <returns>The configuration to use or null if the file should not be spell checked (disabled or not a
        /// type of file that can be spell checked such as a binary file).</returns>
        public SpellCheckerConfiguration GenerateConfiguration(IEnumerable<string> codeAnalysisFiles)
        {
            var config = new SpellCheckerConfiguration();

            try
            {
                foreach(var c in this.ConfigurationFiles)
                    config.Load(c.ConfigFile);

                // Merge any code analysis dictionary settings
                if(codeAnalysisFiles != null)
                    foreach(string cad in codeAnalysisFiles)
                        if(File.Exists(cad))
                            config.ImportCodeAnalysisDictionary(cad);

                // If wanted, set the language based on the resource filename
                if(config.DetermineResourceFileLanguageFromName &&
                  Path.GetExtension(this.Filename).Equals(".resx", StringComparison.OrdinalIgnoreCase))
                {
                    // Localized resource files are expected to have filenames in the format
                    // BaseName.Language.resx (i.e. LocalizedForm.de-DE.resx).
                    string ext = Path.GetExtension(Path.GetFileNameWithoutExtension(this.Filename));

                    if(ext.Length > 1)
                    {
                        ext = ext.Substring(1);

                        if(SpellCheckerDictionary.AvailableDictionaries(
                          config.AdditionalDictionaryFolders).TryGetValue(ext, out SpellCheckerDictionary match))
                        {
                            // Clear any existing dictionary languages and use just the one that matches the
                            // file's language.
                            config.DictionaryLanguages.Clear();
                            config.DictionaryLanguages.Add(match.Culture);
                        }
                    }
                }
            }
            catch(Exception ex)
            {
                // Ignore errors, we just won't load the configurations after the point of failure
                System.Diagnostics.Debug.WriteLine(ex);
            }

            if(!config.IncludeInProjectSpellCheck || config.ShouldExcludeFile(this.CanonicalName) ||
              IsBinaryFile(this.CanonicalName))
            {
                return null;
            }

            this.IgnoredWordsFiles = config.IgnoredWordsFiles;
            return config;
        }

        /// <summary>
        /// This is used to determine whether or not the given file is a binary file
        /// </summary>
        /// <param name="filename">The file to check</param>
        /// <remarks>Since we cannot create an exhaustive list of file types that we cannot spell check, take a
        /// peek at the first 5120 bytes.  If it looks like a binary file, ignore it.  Quick and dirty but mostly
        /// effective.</remarks>
        public static bool IsBinaryFile(string filename)
        {
            bool result = true;

            try
            {
                // If it's not there, ignore it
                if(File.Exists(filename))
                {
                    using(StreamReader sr = new StreamReader(filename, true))
                    {
                        var fileChars = new char[5120];

                        // Note the length as it may be less than the maximum
                        int length = sr.Read(fileChars, 0, fileChars.Length);

                        result = fileChars.Take(length).Any(c => c < 32 && !validChars.Contains(c));
                    }
                }
            }
            catch(Exception ex)
            {
                // Ignore errors, we'll treat it as binary so that it isn't spell checked
                System.Diagnostics.Debug.WriteLine(ex);
            }

            return result;
        }
        #endregion
    }
}
