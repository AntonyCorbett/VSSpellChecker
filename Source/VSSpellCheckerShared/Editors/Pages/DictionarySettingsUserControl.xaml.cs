﻿//===============================================================================================================
// System  : Visual Studio Spell Checker Package
// File    : DictionarySettingsUserControl.xaml.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 09/04/2022
// Note    : Copyright 2014-2022, Eric Woodruff, All rights reserved
//
// This file contains a user control used to edit the spell checker dictionary settings
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://github.com/EWSoftware/VSSpellChecker
// This notice, the author's name, and all copyright notices must remain intact in all applications,
// documentation, and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 06/10/2014  EFW  Moved the language and user dictionary settings to a user control
// 07/22/2015  EFW  Added support for selecting multiple languages
//===============================================================================================================

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

using EnvDTE;
using EnvDTE80;

using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Win32;

using VisualStudio.SpellChecker.Configuration;

using PackageResources = VisualStudio.SpellChecker.Properties.Resources;
using FolderBrowserDlg = System.Windows.Forms.FolderBrowserDialog;

namespace VisualStudio.SpellChecker.Editors.Pages
{
    /// <summary>
    /// This user control is used to edit the spell checker dictionary settings
    /// </summary>
    public partial class DictionarySettingsUserControl : UserControl, ISpellCheckerConfiguration
    {
        #region Private data members
        //=====================================================================

        private ConfigurationType configType;
        private string configFilePath, relatedFilename;
        private bool isGlobal;
        private readonly List<string> selectedLanguages;

        #endregion

        #region Constructor
        //=====================================================================

        public DictionarySettingsUserControl()
        {
            InitializeComponent();

            selectedLanguages = new List<string>();
        }
        #endregion

        #region ISpellCheckerConfiguration Members
        //=====================================================================

        /// <inheritdoc />
        public UserControl Control => this;

        /// <inheritdoc />
        public string Title => "Dictionary Settings";

        /// <inheritdoc />
        public string HelpUrl => "af34b863-6a1c-41ed-bcf2-48a714686519";

        /// <inheritdoc />
        public void LoadConfiguration(SpellingConfigurationFile configuration)
        {
            IEnumerable<string> folders;

            cboAvailableLanguages.ItemsSource = null;
            lbAdditionalFolders.Items.Clear();
            lbSelectedLanguages.Items.Clear();

            var dataSource = new List<PropertyState>();

            if(configuration.ConfigurationType != ConfigurationType.Global)
                dataSource.AddRange(new[] { PropertyState.Inherited, PropertyState.Yes, PropertyState.No });
            else
                dataSource.AddRange(new[] { PropertyState.Yes, PropertyState.No });

            cboDetermineResxLang.ItemsSource = dataSource;

            cboDetermineResxLang.SelectedValue = configuration.ToPropertyState(
                PropertyNames.DetermineResourceFileLanguageFromName);

            configType = configuration.ConfigurationType;
            relatedFilename = Path.GetFileNameWithoutExtension(configuration.Filename);
            configFilePath = Path.GetDirectoryName(configuration.Filename);
            isGlobal = configuration.ConfigurationType == ConfigurationType.Global;

            if(relatedFilename.Length == 0)
                relatedFilename = ".vsspell";

            if(isGlobal)
            {
                chkInheritAdditionalFolders.IsChecked = false;
                chkInheritAdditionalFolders.Visibility = Visibility.Collapsed;
            }
            else
                chkInheritAdditionalFolders.IsChecked = configuration.ToBoolean(
                    PropertyNames.InheritAdditionalDictionaryFolders);

            if(configuration.HasProperty(PropertyNames.AdditionalDictionaryFolders))
            {
                folders = configuration.ToValues(PropertyNames.AdditionalDictionaryFolders,
                    PropertyNames.AdditionalDictionaryFoldersItem);
            }
            else
                folders = Enumerable.Empty<string>();

            foreach(string f in folders)
                lbAdditionalFolders.Items.Add(f);

            var sd = new SortDescription { Direction = ListSortDirection.Ascending };

            lbAdditionalFolders.Items.SortDescriptions.Add(sd);

            selectedLanguages.Clear();
            selectedLanguages.AddRange(configuration.ToValues(PropertyNames.SelectedLanguages,
              PropertyNames.SelectedLanguagesItem, true).Distinct(StringComparer.OrdinalIgnoreCase));

#pragma warning disable VSTHRD010
            this.LoadAvailableLanguages();
#pragma warning restore VSTHRD010
        }

        /// <inheritdoc />
        public void SaveConfiguration(SpellingConfigurationFile configuration)
        {
            HashSet<string> newList = null;

            configuration.StoreProperty(PropertyNames.DetermineResourceFileLanguageFromName,
                ((PropertyState)cboDetermineResxLang.SelectedValue).ToPropertyValue());

            relatedFilename = Path.GetFileNameWithoutExtension(configuration.Filename);
            configFilePath = Path.GetDirectoryName(configuration.Filename);
            isGlobal = configuration.ConfigurationType == ConfigurationType.Global;

            if(relatedFilename.Length == 0)
                relatedFilename = ".vsspell";

            if(lbAdditionalFolders.Items.Count != 0)
                newList = new HashSet<string>(lbAdditionalFolders.Items.Cast<string>(),
                    StringComparer.OrdinalIgnoreCase);

            if(!isGlobal)
                configuration.StoreProperty(PropertyNames.InheritAdditionalDictionaryFolders,
                    chkInheritAdditionalFolders.IsChecked);

            configuration.StoreValues(PropertyNames.AdditionalDictionaryFolders,
                PropertyNames.AdditionalDictionaryFoldersItem, newList);

            configuration.StoreValues(PropertyNames.SelectedLanguages, PropertyNames.SelectedLanguagesItem,
                lbSelectedLanguages.Items.Cast<SpellCheckerDictionary>().Select(d => d.Culture.Name));
        }

        /// <inheritdoc />
        public bool AppliesTo(ConfigurationType configurationType)
        {
            return true;
        }

        /// <inheritdoc />
        public event EventHandler ConfigurationChanged;

        #endregion

        #region Helper methods
        //=====================================================================

        /// <summary>
        /// This loads the list of available dictionary languages
        /// </summary>
        /// <remarks>This will find all dictionaries in the global configuration folder as well as any additional
        /// folders specified in this configuration file.</remarks>
        private void LoadAvailableLanguages()
        {
            SpellCheckerDictionary match = null;
            List<SpellCheckerDictionary> availableDictionaries = new List<SpellCheckerDictionary>();
            CultureInfo defaultLang = null;

            if(cboAvailableLanguages.Items.Count != 0)
            {
                defaultLang = ((SpellCheckerDictionary)cboAvailableLanguages.SelectedItem).Culture;

                selectedLanguages.Clear();
                selectedLanguages.AddRange(lbSelectedLanguages.Items.Cast<SpellCheckerDictionary>().Select(
                    d => d.Culture.Name));
            }

            cboAvailableLanguages.ItemsSource = null;

            if(!isGlobal)
                availableDictionaries.Add(new SpellCheckerDictionary(CultureInfo.InvariantCulture, null, null,
                    null, false));

            List<string> additionalFolders = new List<string>();

            // Include inherited additional folders from parent configurations.  This allows for consistent
            // user dictionary content across all configuration files within a solution and/or project
            if(chkInheritAdditionalFolders.IsChecked.Value)
            {
#pragma warning disable VSTHRD010
                var parentConfig = this.GenerateParentConfiguration();
#pragma warning restore VSTHRD010

                additionalFolders.AddRange(parentConfig.AdditionalDictionaryFolders);
            }

            // Fully qualify relative paths with the configuration file path
            foreach(string folder in lbAdditionalFolders.Items.Cast<string>())
            {
                if(folder.IndexOf('%') != -1 || Path.IsPathRooted(folder))
                    additionalFolders.Add(folder);
                else
                    additionalFolders.Add(Path.GetFullPath(Path.Combine(configFilePath, folder)));
            }

            foreach(var lang in SpellCheckerDictionary.AvailableDictionaries(
              additionalFolders.Distinct()).Values.OrderBy(d => d.ToString()))
            {
                availableDictionaries.Add(lang);
            }

            cboAvailableLanguages.ItemsSource = availableDictionaries;

            // Add selected languages first
            if(selectedLanguages.Count != 0)
            {
                lbSelectedLanguages.Items.Clear();

                foreach(string language in selectedLanguages)
                {
                    match = availableDictionaries.FirstOrDefault(d => d.Culture.Name.Equals(language,
                        StringComparison.OrdinalIgnoreCase));

                    if(match != null)
                        lbSelectedLanguages.Items.Add(match);
                }
            }

            // Then set the default language selection for the user dictionary
            if(defaultLang != null)
            {
                match = availableDictionaries.FirstOrDefault(d => d.Culture.Name == defaultLang.Name);

                if(match != null)
                    cboAvailableLanguages.SelectedItem = match;
                else
                    cboAvailableLanguages.SelectedIndex = 0;
            }
            else
            {
                if(lbSelectedLanguages.Items.Count != 0)
                {
                    var primary = (SpellCheckerDictionary)lbSelectedLanguages.Items[0];

                    match = availableDictionaries.FirstOrDefault(d => d.Culture.Name == primary.Culture.Name);
                }
                else
                    if(isGlobal)
                        match = availableDictionaries.FirstOrDefault(d => d.Culture.Name == "en-US");

                if(match != null)
                    cboAvailableLanguages.SelectedItem = match;
                else
                    if(cboAvailableLanguages.Items.Count != 0)
                        cboAvailableLanguages.SelectedIndex = 0;
            }

            lbSelectedLanguages_SelectionChanged(this, null);
        }

        /// <summary>
        /// Generate the configuration for all parent items
        /// </summary>
        /// <returns>The generated configuration to use</returns>
        /// <remarks>The configuration is a merger of the global settings plus any solution, project, and folder
        /// settings related to but excluding the current configuration file.  This allows us to determine the
        /// inherited additional dictionary folders to use.</remarks>
        private SpellCheckerConfiguration GenerateParentConfiguration()
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();

            ProjectItem projectItem, fileItem;
            string filename, projectPath;

            // Start with the global configuration
            var config = new SpellCheckerConfiguration();

            if(isGlobal)
                return config;

            try
            {
                config.Load(SpellingConfigurationFile.GlobalConfigurationFilename);

                if(configType == ConfigurationType.Solution)
                    return config;

                var dte2 = Utility.GetServiceFromPackage<DTE2, SDTE>(true);

                if(dte2 != null && dte2.Solution != null && !String.IsNullOrWhiteSpace(dte2.Solution.FullName))
                {
                    var solution = dte2.Solution;

                    // See if there is a solution configuration
                    filename = solution.FullName + ".vsspell";
                    projectItem = solution.FindProjectItemForFile(filename);

                    // Allow for solution configuration files to be named ".vsspell"
                    if(projectItem == null)
                    {
                        filename = Path.Combine(Path.GetDirectoryName(filename), ".vsspell");
                        projectItem = solution.FindProjectItemForFile(filename);
                    }

                    if(projectItem != null)
                        config.Load(filename);

                    if(configType == ConfigurationType.Project)
                        return config;

                    // Find the project item for the file we are opening
                    if(configType != ConfigurationType.Folder)
                        projectItem = solution.FindProjectItemForFile(Path.Combine(configFilePath, relatedFilename));
                    else
                        projectItem = solution.FindProjectItemForFile(Path.Combine(configFilePath, relatedFilename + ".vsspell"));

                    if(projectItem != null)
                    {
                        fileItem = projectItem;

                        // If we have a project (we should), see if it has settings
                        if(projectItem.ContainingProject != null &&
                          !String.IsNullOrWhiteSpace(projectItem.ContainingProject.FullName))
                        {
                            filename = projectItem.ContainingProject.FullName + ".vsspell";
                            projectItem = solution.FindProjectItemForFile(filename);

                            if(projectItem != null)
                                config.Load(filename);

                            // Get the full path based on the project.  The configuration filename will refer to
                            // the actual path which may be to a linked file outside the project's folder
                            // structure.
                            projectPath = Path.GetDirectoryName(filename);
                            filename = Path.GetDirectoryName((string)fileItem.Properties.Item("FullPath").Value);

                            // Search for folder-specific configuration files
                            if(filename.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
                            {
                                // Then check subfolders.  No need to check the root folder as the project
                                // settings cover it.
                                if(filename.Length > projectPath.Length)
                                    foreach(string folder in filename.Substring(projectPath.Length + 1).Split('\\'))
                                    {
                                        projectPath = Path.Combine(projectPath, folder);
                                        filename = Path.Combine(projectPath, folder + ".vsspell");

                                        if(configType == ConfigurationType.Folder &&
                                          Path.GetFileNameWithoutExtension(filename) == relatedFilename)
                                            return config;

                                        projectItem = solution.FindProjectItemForFile(filename);

                                        if(projectItem != null)
                                            config.Load(filename);
                                    }
                            }

                            // If the item looks like a dependent file item, look for a settings file related to
                            // the parent file item.
                            if(fileItem.Collection != null && fileItem.Collection.Parent != null)
                            {
                                projectItem = fileItem.Collection.Parent as ProjectItem;

                                if(projectItem != null && projectItem.Kind == EnvDTE.Constants.vsProjectItemKindPhysicalFile)
                                {
                                    filename = (string)projectItem.Properties.Item("FullPath").Value + ".vsspell";
                                    projectItem = solution.FindProjectItemForFile(filename);

                                    if(projectItem != null)
                                        config.Load(filename);
                                }
                            }
                        }
                    }
                }
            }
            catch(Exception ex)
            {
                // Ignore errors, we just won't load the configurations after the point of failure
                System.Diagnostics.Debug.WriteLine(ex);
            }

            return config;
        }
        #endregion

        #region Event handlers
        //=====================================================================

        /// <summary>
        /// Select a folder to add
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The event arguments</param>
        private void btnSelectFolder_Click(object sender, RoutedEventArgs e)
        {
            using(FolderBrowserDlg dlg = new FolderBrowserDlg())
            {
                dlg.Description = "Select an additional dictionary folder";
                dlg.SelectedPath = !isGlobal && Directory.Exists(configFilePath) ? configFilePath :
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                if(dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    txtAdditionalFolder.Text = dlg.SelectedPath;
#pragma warning disable VSTHRD010
                    btnAddFolder_Click(sender, e);
#pragma warning restore VSTHRD010
                }
            }
        }

        /// <summary>
        /// Add a new additional folder to the list
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The event arguments</param>
        private void btnAddFolder_Click(object sender, RoutedEventArgs e)
        {
            txtAdditionalFolder.Text = txtAdditionalFolder.Text.Trim();

            if(txtAdditionalFolder.Text.Length != 0)
            {
                if(!isGlobal && MessageBox.Show("Would you like to make the path relative to the current " +
                  "configuration file?", PackageResources.PackageTitle, MessageBoxButton.YesNo,
                  MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
                {
                    txtAdditionalFolder.Text = txtAdditionalFolder.Text.ToRelativePath(configFilePath);

                    if(txtAdditionalFolder.Text.Length == 0 || txtAdditionalFolder.Text[0] != '.')
                        txtAdditionalFolder.Text = ".\\" + txtAdditionalFolder.Text;
                }

                string folder = txtAdditionalFolder.Text;

                if(folder.IndexOf('%') != -1)
                    folder = Environment.ExpandEnvironmentVariables(folder);
                else
                    if(!Path.IsPathRooted(folder))
                        folder = Path.Combine(configFilePath, folder);

                if(Directory.Exists(folder))
                {
                    lbAdditionalFolders.Items.Add(txtAdditionalFolder.Text);
                    txtAdditionalFolder.Text = null;

#pragma warning disable VSTHRD010
                    Property_Changed(sender, e);

                    this.LoadAvailableLanguages();
#pragma warning restore VSTHRD010
                }
                else
                    MessageBox.Show("The specified folder does not appear to exist", PackageResources.PackageTitle,
                        MessageBoxButton.OK, MessageBoxImage.Exclamation);
            }
        }

        /// <summary>
        /// Remove the selected additional folder from the list
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The event arguments</param>
        private void btnRemoveFolder_Click(object sender, RoutedEventArgs e)
        {
            int idx = lbAdditionalFolders.SelectedIndex;

            if(idx != -1)
                lbAdditionalFolders.Items.RemoveAt(idx);

            if(lbAdditionalFolders.Items.Count != 0)
            {
                if(idx < 0)
                    idx = 0;
                else
                    if(idx >= lbAdditionalFolders.Items.Count)
                        idx = lbAdditionalFolders.Items.Count - 1;

                lbAdditionalFolders.SelectedIndex = idx;
            }

#pragma warning disable VSTHRD010
            Property_Changed(sender, e);

            this.LoadAvailableLanguages();
#pragma warning restore VSTHRD010
        }

        /// <summary>
        /// Clear the list of additional folders
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The event arguments</param>
        private void btnClearFolders_Click(object sender, RoutedEventArgs e)
        {
            lbAdditionalFolders.Items.Clear();

            var sd = new SortDescription { Direction = ListSortDirection.Ascending };
            lbAdditionalFolders.Items.SortDescriptions.Add(sd);

#pragma warning disable VSTHRD010
            Property_Changed(sender, e);

            this.LoadAvailableLanguages();
#pragma warning restore VSTHRD010
        }

        /// <summary>
        /// Load the user dictionary file when the selected language changes
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The event arguments</param>
        private void cboAvailableLanguages_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            lbUserDictionary.Items.Clear();
            grpUserDictionary.IsEnabled = false;

            if(cboAvailableLanguages.Items.Count != 0 && cboAvailableLanguages.SelectedItem != null)
            {
                if(cboAvailableLanguages.SelectedItem.ToString() != "Inherited")
                {
                    var dictionary = (SpellCheckerDictionary)cboAvailableLanguages.SelectedItem;
                    string filename = dictionary.UserDictionaryFilePath;

                    grpUserDictionary.IsEnabled = true;
                    grpUserDictionary.Header = "_User Dictionary (" + dictionary.Culture.Name + ")";

                    lblDictionaryType.Content = dictionary.IsCustomDictionary ? "Custom dictionary" :
                        "Package dictionary";
                    lblDictionaryType.ToolTip = "Location: " + (dictionary.IsCustomDictionary ?
                        Path.GetDirectoryName(dictionary.DictionaryFilePath) : "Package folder");

                    lblUserDictionaryType.Content = dictionary.HasAlternateUserDictionary ?
                        "Alternate user dictionary" : "Standard user dictionary";
                    lblUserDictionaryType.ToolTip = "Location: " + Path.GetDirectoryName(dictionary.UserDictionaryFilePath);

                    if(File.Exists(filename))
                        try
                        {
                            foreach(string word in File.ReadAllLines(filename))
                                lbUserDictionary.Items.Add(word);
                        }
                        catch(Exception ex)
                        {
                            MessageBox.Show("Unable to load user dictionary.  Reason: " + ex.Message,
                                PackageResources.PackageTitle, MessageBoxButton.OK, MessageBoxImage.Exclamation);
                        }
                        finally
                        {
                            var sd = new SortDescription { Direction = ListSortDirection.Ascending };
                            lbUserDictionary.Items.SortDescriptions.Add(sd);
                        }
                }
                else
                {
                    grpUserDictionary.Header = "_User Dictionary";
                    lblDictionaryType.Content = lblUserDictionaryType.Content = lblDictionaryType.ToolTip =
                        lblUserDictionaryType.ToolTip = String.Empty;
                }
            }
        }

        /// <summary>
        /// Update the button states when the selected index changes
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The event arguments</param>
        private void lbSelectedLanguages_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            btnAddLanguage.IsEnabled = (cboAvailableLanguages.Items.Count != 0);
            btnRemoveLanguage.IsEnabled = (lbSelectedLanguages.Items.Count != 0);
            btnMoveLanguageUp.IsEnabled = (lbSelectedLanguages.SelectedIndex > 0);
            btnMoveLanguageDown.IsEnabled = (lbSelectedLanguages.SelectedIndex != lbSelectedLanguages.Items.Count - 1);

            if(lbSelectedLanguages.SelectedItem != null)
                cboAvailableLanguages.SelectedItem = lbSelectedLanguages.SelectedItem;

            if(lbSelectedLanguages.Items.Count != 0)
            {
                if(lblAddLanguage.Visibility == Visibility.Visible)
                    lblAddLanguage.Visibility = Visibility.Collapsed;
            }
            else
                if(lblAddLanguage.Visibility == Visibility.Collapsed)
                {
                    lblAddLanguage.Visibility = Visibility.Visible;

                    if(!isGlobal)
                        lblAddLanguage.Text = "Add a language here if you want to spell check using something " +
                            "other than the inherited language(s).";
                }
        }

        /// <summary>
        /// Add the selected language to the list
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The event arguments</param>
        private void btnAddLanguage_Click(object sender, RoutedEventArgs e)
        {
            if(cboAvailableLanguages.Items.Count != 0 && cboAvailableLanguages.SelectedItem != null &&
              !lbSelectedLanguages.Items.Contains(cboAvailableLanguages.SelectedItem))
            {
                lbSelectedLanguages.SelectedIndex = lbSelectedLanguages.Items.Add(cboAvailableLanguages.SelectedItem);
                lbSelectedLanguages.ScrollIntoView(lbSelectedLanguages.SelectedItem);
#pragma warning disable VSTHRD010
                Property_Changed(sender, e);
#pragma warning restore VSTHRD010
            }
        }

        /// <summary>
        /// Remove the selected language from the list
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The event arguments</param>
        private void btnRemoveLanguage_Click(object sender, RoutedEventArgs e)
        {
            if(lbSelectedLanguages.SelectedItem != null)
            {
                int idx = lbSelectedLanguages.SelectedIndex;

                lbSelectedLanguages.Items.Remove(lbSelectedLanguages.SelectedItem);

                if(idx >= lbSelectedLanguages.Items.Count)
                    idx = lbSelectedLanguages.Items.Count - 1;

                lbSelectedLanguages.SelectedIndex = idx;

#pragma warning disable VSTHRD010
                Property_Changed(sender, e);
#pragma warning restore VSTHRD010
            }
        }

        /// <summary>
        /// Move the selected language up in the list
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The event arguments</param>
        private void btnMoveLanguageUp_Click(object sender, RoutedEventArgs e)
        {
            if(lbSelectedLanguages.SelectedItem != null)
            {
                object item = lbSelectedLanguages.SelectedItem;
                int idx = lbSelectedLanguages.SelectedIndex;

                if(idx - 1 >= 0)
                {
                    lbSelectedLanguages.Items.Remove(item);
                    lbSelectedLanguages.Items.Insert(idx - 1, item);
                    lbSelectedLanguages.SelectedIndex = idx - 1;

#pragma warning disable VSTHRD010
                    Property_Changed(sender, e);
#pragma warning restore VSTHRD010
                }
            }
        }

        /// <summary>
        /// Move the selected language down in the list
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The event arguments</param>
        private void btnMoveLanguageDown_Click(object sender, RoutedEventArgs e)
        {
            if(lbSelectedLanguages.SelectedItem != null)
            {
                object item = lbSelectedLanguages.SelectedItem;
                int idx = lbSelectedLanguages.SelectedIndex;

                if(idx < lbSelectedLanguages.Items.Count - 1)
                {
                    lbSelectedLanguages.Items.Remove(item);
                    lbSelectedLanguages.Items.Insert(idx + 1, item);
                    lbSelectedLanguages.SelectedIndex = idx + 1;

#pragma warning disable VSTHRD010
                    Property_Changed(sender, e);
#pragma warning restore VSTHRD010
                }
            }
        }

        /// <summary>
        /// Remove the selected word from the user dictionary
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The event arguments</param>
        private void btnRemoveDictionaryWord_Click(object sender, RoutedEventArgs e)
        {
            int idx = lbUserDictionary.SelectedIndex;
            string word = null;

            var selectedDictionary = (SpellCheckerDictionary)cboAvailableLanguages.SelectedItem;

            if(!GlobalDictionary.IsReadyForUse(selectedDictionary.Culture))
            {
                MessageBox.Show("The selected dictionary is still loading.  Please try again in a few seconds.",
                    PackageResources.PackageTitle, MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }

            if(idx != -1)
            {
                word = (string)lbUserDictionary.Items[idx];
                lbUserDictionary.Items.RemoveAt(idx);
            }

            if(lbUserDictionary.Items.Count != 0)
            {
                if(idx < 0)
                    idx = 0;
                else
                    if(idx >= lbUserDictionary.Items.Count)
                        idx = lbUserDictionary.Items.Count - 1;

                lbUserDictionary.SelectedIndex = idx;
            }

            try
            {
#pragma warning disable VSTHRD010
                if(selectedDictionary.UserDictionaryFilePath.CanWriteToUserWordsFile(
                  selectedDictionary.DictionaryFilePath))
                {
                    File.WriteAllLines(selectedDictionary.UserDictionaryFilePath,
                        lbUserDictionary.Items.Cast<string>());

                    if(!String.IsNullOrWhiteSpace(word))
                        GlobalDictionary.RemoveWord(selectedDictionary.Culture, word);

                    GlobalDictionary.LoadUserDictionaryFile(selectedDictionary.Culture);
                }
                else
                    MessageBox.Show("Unable to save user dictionary.  The file could not be added to the " +
                        "project, could not be checked out, or is read-only", PackageResources.PackageTitle,
                        MessageBoxButton.OK, MessageBoxImage.Exclamation);
#pragma warning restore VSTHRD010
            }
            catch(Exception ex)
            {
                MessageBox.Show("Unable to save user dictionary.  Reason: " + ex.Message,
                    PackageResources.PackageTitle, MessageBoxButton.OK, MessageBoxImage.Exclamation);
            }
        }

        /// <summary>
        /// Import words from a user dictionary file
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The event arguments</param>
        private void btnImport_Click(object sender, RoutedEventArgs e)
        {
            var selectedDictionary = (SpellCheckerDictionary)cboAvailableLanguages.SelectedItem;

            if(!GlobalDictionary.IsReadyForUse(selectedDictionary.Culture))
            {
                MessageBox.Show("The selected dictionary is still loading.  Please try again in a few seconds.",
                    PackageResources.PackageTitle, MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }

            OpenFileDialog dlg = new OpenFileDialog
            {
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Filter = "User Dictionary Files (*.dic,*.xml)|*.dic;*.xml|" +
                    "StyleCop Settings Files (*.stylecop)|*.stylecop|Text documents (*.txt)|*.txt|" +
                    "All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if(dlg.ShowDialog() ?? false)
            {
                try
                {
                    var uniqueWords = new HashSet<string>(Utility.LoadUserDictionary(dlg.FileName, true, false),
                        StringComparer.OrdinalIgnoreCase);

                    if(uniqueWords.Count == 0)
                    {
                        MessageBox.Show("Unable to load any words from the selected file", PackageResources.PackageTitle,
                            MessageBoxButton.OK, MessageBoxImage.Exclamation);
                        return;
                    }

                    if(lbUserDictionary.Items.Count != 0 && MessageBox.Show("Do you want to replace the " +
                      "existing list of words?  Click Yes to replace them or No to merge the new words into " +
                      "the existing list.", PackageResources.PackageTitle, MessageBoxButton.YesNo,
                      MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.No)
                    {
                        uniqueWords.UnionWith(lbUserDictionary.Items.Cast<string>());
                    }

                    try
                    {
#pragma warning disable VSTHRD010
                        if(selectedDictionary.UserDictionaryFilePath.CanWriteToUserWordsFile(
                          selectedDictionary.DictionaryFilePath))
                        {
                            File.WriteAllLines(selectedDictionary.UserDictionaryFilePath, uniqueWords);

                            GlobalDictionary.LoadUserDictionaryFile(selectedDictionary.Culture);

                            cboAvailableLanguages_SelectionChanged(sender, new SelectionChangedEventArgs(
                                e.RoutedEvent, Array.Empty<object>(), Array.Empty<object>()));
                        }
                        else
                            MessageBox.Show("Unable to save user dictionary.  The file could not be added to " +
                                "the project, could not be checked out, or is read-only.",
                                PackageResources.PackageTitle, MessageBoxButton.OK, MessageBoxImage.Exclamation);
#pragma warning restore VSTHRD010
                    }
                    catch(Exception ex)
                    {
                        MessageBox.Show("Unable to save user dictionary.  Reason: " + ex.Message,
                            PackageResources.PackageTitle, MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    }
                }
                catch(Exception ex)
                {
                    MessageBox.Show(String.Format(CultureInfo.CurrentCulture, "Unable to load user dictionary " +
                        "from '{0}'.  Reason: {1}", dlg.FileName, ex.Message), PackageResources.PackageTitle,
                        MessageBoxButton.OK, MessageBoxImage.Exclamation);
                }
            }
        }

        /// <summary>
        /// Export words to a user dictionary file
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The event arguments</param>
        private void btnExport_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dlg = new SaveFileDialog
            {
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                FileName = "UserDictionary.dic",
                DefaultExt = ".dic",
                OverwritePrompt = false,
                Filter = "User Dictionary Files (*.dic,*.xml)|*.dic;*.xml|Text documents (*.txt)|*.txt|" +
                    "All Files (*.*)|*.*"
            };

            if((dlg.ShowDialog() ?? false))
            {
                try
                {
                    var uniqueWords = new HashSet<string>(lbUserDictionary.Items.Cast<string>(),
                        StringComparer.OrdinalIgnoreCase);
                    bool replaceWords = true;

                    if(File.Exists(dlg.FileName))
                    {
#pragma warning disable VSTHRD010
                        if(!dlg.FileName.CanWriteToUserWordsFile(null))
                        {
                            MessageBox.Show("File is read-only or could not be checked out",
                                PackageResources.PackageTitle, MessageBoxButton.OK, MessageBoxImage.Exclamation);
                            return;
                        }
#pragma warning restore VSTHRD010

                        MessageBoxResult result = MessageBox.Show("Do you want to replace the words in the " +
                          "existing file?  Click Yes to replace them, No to merge the new words into the " +
                          "existing file, or Cancel to stop and do nothing.", PackageResources.PackageTitle,
                          MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.No);

                        if(result == MessageBoxResult.Cancel)
                            return;

                        if(result == MessageBoxResult.No)
                        {
                            uniqueWords.UnionWith(Utility.LoadUserDictionary(dlg.FileName, true, true));
                            replaceWords = false;
                        }
                    }

                    Utility.SaveCustomDictionary(dlg.FileName, replaceWords, true, uniqueWords);
                }
                catch(Exception ex)
                {
                    MessageBox.Show(String.Format(CultureInfo.CurrentCulture, "Unable to save user dictionary " +
                        "to '{0}'.  Reason: {1}", dlg.FileName, ex.Message), PackageResources.PackageTitle,
                        MessageBoxButton.OK, MessageBoxImage.Exclamation);
                }
            }
        }

        /// <summary>
        /// Notify the parent of property changes that affect the file's dirty state
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The event arguments</param>
        private void Property_Changed(object sender, RoutedEventArgs e)
        {
            this.ConfigurationChanged?.Invoke(this, EventArgs.Empty);

#pragma warning disable VSTHRD010
            if(sender == chkInheritAdditionalFolders && cboAvailableLanguages.Items.Count != 0)
                this.LoadAvailableLanguages();
#pragma warning restore VSTHRD010
        }
        #endregion
    }
}
