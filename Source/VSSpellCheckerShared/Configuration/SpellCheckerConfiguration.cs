﻿//===============================================================================================================
// System  : Visual Studio Spell Checker Package
// File    : SpellCheckerConfiguration.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 01/13/2021
// Note    : Copyright 2015-2021, Eric Woodruff, All rights reserved
//
// This file contains the class used to contain the spell checker's configuration settings
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://github.com/EWSoftware/VSSpellChecker
// This notice, the author's name, and all copyright notices must remain intact in all applications,
// documentation, and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 02/01/2015  EFW  Refactored the configuration settings to allow for solution and project specific settings
// 07/22/2015  EFW  Added support for selecting multiple languages
// 08/15/2018  EFW  Added support for tracking and excluding classifications using the classification cache
//===============================================================================================================

// Ignore spelling: lt cebf

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.XPath;

namespace VisualStudio.SpellChecker.Configuration
{
    /// <summary>
    /// This class is used to contain the spell checker's configuration
    /// </summary>
    /// <remarks>Settings are stored in an XML file in the user's local application data folder and will be used
    /// by all versions of Visual Studio in which the package is installed.</remarks>
    public class SpellCheckerConfiguration
    {
        #region Private data members
        //=====================================================================

        private HashSet<string> ignoredWords, ignoredXmlElements, spellCheckedXmlAttributes; 
        private readonly HashSet<string> recognizedWords, loadedConfigFiles;
        private readonly List<(ConfigurationType ConfigType, string Filename)> ignoredWordsFiles;
        private List<CultureInfo> dictionaryLanguages;
        private List<string> additionalDictionaryFolders;
        private List<Regex> exclusionExpressions, visualStudioExclusions;
        private readonly List<Regex> ignoredFilePatterns;

        private readonly Dictionary<string, HashSet<string>> ignoredClassifications;
        private readonly Dictionary<string, string> deprecatedTerms, compoundTerms;
        private readonly Dictionary<string, IList<string>> unrecognizedWords;

        #endregion

        #region Properties
        //=====================================================================

        /// <summary>
        /// This read-only property returns a list of dictionary languages to be used when spell checking
        /// </summary>
        public IList<CultureInfo> DictionaryLanguages
        {
            get
            {
                // Always ensure we have at least the default language if no configuration was loaded
                if(dictionaryLanguages.Count == 0)
                    dictionaryLanguages.Add(new CultureInfo("en-US"));

                return dictionaryLanguages;
            }
        }

        /// <summary>
        /// This is used to get or set whether or not to spell check the file as you type
        /// </summary>
        /// <value>This is true by default</value>
        [DefaultValue(true)]
        public bool SpellCheckAsYouType { get; set; }

        /// <summary>
        /// This is used to get or set whether or not to spell check the file as part of the solution/project
        /// spell checking process.
        /// </summary>
        /// <value>This is true by default</value>
        [DefaultValue(true)]
        public bool IncludeInProjectSpellCheck { get; set; }

        /// <summary>
        /// This is used to get or set whether or not to detect doubled words as part of the spell checking
        /// process.
        /// </summary>
        /// <value>This is true by default</value>
        [DefaultValue(true)]
        public bool DetectDoubledWords { get; set; }

        /// <summary>
        /// This is used to get or set whether or not to ignore words containing digits
        /// </summary>
        /// <value>This is true by default</value>
        [DefaultValue(true)]
        public bool IgnoreWordsWithDigits { get; set; }

        /// <summary>
        /// This is used to get or set whether or not to ignore words in all uppercase
        /// </summary>
        /// <value>This is true by default</value>
        [DefaultValue(true)]
        public bool IgnoreWordsInAllUppercase { get; set; }

        /// <summary>
        /// This is used to get or set whether or not to ignore words in mixed/camel case
        /// </summary>
        /// <value>This is true by default</value>
        [DefaultValue(true)]
        public bool IgnoreWordsInMixedCase { get; set; }

        /// <summary>
        /// This is used to get or set whether or not to ignore .NET and C-style format string specifiers
        /// </summary>
        /// <value>This is true by default</value>
        [DefaultValue(true)]
        public bool IgnoreFormatSpecifiers { get; set; }

        /// <summary>
        /// This is used to get or set whether or not to ignore words that look like filenames or e-mail
        /// addresses.
        /// </summary>
        /// <value>This is true by default</value>
        [DefaultValue(true)]
        public bool IgnoreFilenamesAndEMailAddresses { get; set; }

        /// <summary>
        /// This is used to get or set whether or not to ignore XML elements in the text being spell checked
        /// (text within '&amp;lt;' and '&amp;gt;').
        /// </summary>
        /// <value>This is true by default</value>
        [DefaultValue(true)]
        public bool IgnoreXmlElementsInText { get; set; }

        /// <summary>
        /// This is used to get or set whether or not to ignore words by character class
        /// </summary>
        /// <remarks>This provides a simplistic way of ignoring some words in mixed language files.  It works
        /// best for spell checking English text in files that also contain Cyrillic or Asian text.  The default
        /// is <c>None</c> to include all words regardless of the characters they contain.</remarks>
        [DefaultValue(IgnoredCharacterClass.None)]
        public IgnoredCharacterClass IgnoreCharacterClass { get; set; }

        /// <summary>
        /// This is used to get or set whether or not underscores are treated as a word separator
        /// </summary>
        /// <value>This is false by default</value>
        [DefaultValue(false)]
        public bool TreatUnderscoreAsSeparator { get; set; }

        /// <summary>
        /// This is used to get or set whether or not mnemonics are ignored within words
        /// </summary>
        /// <value>This is true by default.  If false, mnemonic characters act as word breaks.</value>
        [DefaultValue(true)]
        public bool IgnoreMnemonics { get; set; }

        /// <summary>
        /// This is used to get or set whether or not to try to determine the language for resource files based
        /// on their filename (i.e. LocalizedForm.de-DE.resx).
        /// </summary>
        /// <value>This is true by default</value>
        [DefaultValue(true)]
        public bool DetermineResourceFileLanguageFromName { get; set; }

        /// <summary>
        /// This read-only property returns the C# source code file options
        /// </summary>
        public CSharpOptions CSharpOptions { get; }

        /// <summary>
        /// This read-only property returns the code analysis dictionary options
        /// </summary>
        public CodeAnalysisDictionaryOptions CadOptions { get; }

        /// <summary>
        /// This is used to indicate whether or not ignored file patterns are inherited by other configurations
        /// </summary>
        /// <value>The default is true so that sub-configurations inherit all ignored files from higher level
        /// configurations.</value>
        [DefaultValue(true)]
        public bool InheritIgnoredFilePatterns { get; set; }

        /// <summary>
        /// This read-only property returns an enumerable list of ignored file patterns
        /// </summary>
        /// <remarks>Filenames matching the patterns in this set will not be spell checked</remarks>
        public IEnumerable<Regex> IgnoredFilePatterns => ignoredFilePatterns;

        /// <summary>
        /// This is used to indicate whether or not additional dictionary folders are inherited by other
        /// configurations.
        /// </summary>
        /// <value>The default is true so that sub-configurations inherit all additional dictionary folders from
        /// higher level configurations.</value>
        [DefaultValue(true)]
        public bool InheritAdditionalDictionaryFolders { get; set; }

        /// <summary>
        /// This read-only property returns an enumerable list of additional dictionary folders
        /// </summary>
        /// <remarks>When searching for dictionaries, these folders will be included in the search.  This allows
        /// for solution and project-specific dictionaries.</remarks>
        public IEnumerable<string> AdditionalDictionaryFolders => additionalDictionaryFolders;

        /// <summary>
        /// This is used to indicate whether or not ignored words are inherited by other configurations
        /// </summary>
        /// <value>The default is true so that sub-configurations inherit all ignored words from higher level
        /// configurations.</value>
        [DefaultValue(true)]
        public bool InheritIgnoredWords { get; set; }

        /// <summary>
        /// This read-only property returns an enumerable list of ignored words that will not be spell checked
        /// </summary>
        public IEnumerable<string> IgnoredWords => ignoredWords;

        /// <summary>
        /// This read-only property returns an enumerable list of ignored words files that were loaded by this
        /// configuration.
        /// </summary>
        public IEnumerable<(ConfigurationType ConfigType, string Filename)> IgnoredWordsFiles
        {
            get
            {
                // In a brand new configuration with no saved settings, ensure that at least the default global
                // ignored words file is returned.
                if(ignoredWordsFiles.Count == 0)
                {
                    string ignoredWordsFile = Path.Combine(SpellingConfigurationFile.GlobalConfigurationFilePath,
                        "IgnoredWords.dic");

                    ignoredWordsFiles.Add((ConfigurationType.Global, ignoredWordsFile));
                }

                return ignoredWordsFiles;
            }
        }

        /// <summary>
        /// This is used to indicate whether or not exclusion expressions are inherited by other configurations
        /// </summary>
        /// <value>The default is true so that sub-configurations inherit all exclusion expressions from higher
        /// level configurations.</value>
        [DefaultValue(true)]
        public bool InheritExclusionExpressions { get; set; }

        /// <summary>
        /// This read-only property returns an enumerable list of exclusion regular expressions that will be used
        /// to find ranges of text that should not be spell checked.
        /// </summary>
        public IEnumerable<Regex> ExclusionExpressions => exclusionExpressions;

        /// <summary>
        /// This is used to indicate whether or not ignored XML elements and included attributes are inherited by
        /// other configurations.
        /// </summary>
        /// <value>The default is true so that sub-configurations inherit all ignored XML elements and included
        /// attributes from higher level configurations.</value>
        [DefaultValue(true)]
        public bool InheritXmlSettings { get; set; }

        /// <summary>
        /// This read-only property returns an enumerable list of ignored XML element names that will not have
        /// their content spell checked.
        /// </summary>
        public IEnumerable<string> IgnoredXmlElements => ignoredXmlElements;

        /// <summary>
        /// This read-only property returns an enumerable list of XML attribute names that will not have their
        /// values spell checked.
        /// </summary>
        public IEnumerable<string> SpellCheckedXmlAttributes => spellCheckedXmlAttributes;

        /// <summary>
        /// This is used to indicate whether or not ignored classifications are inherited by other configurations
        /// </summary>
        /// <value>The default is true so that sub-configurations inherit all ignored classifications from higher
        /// level configurations.</value>
        [DefaultValue(true)]
        public bool InheritIgnoredClassifications { get; set; }

        /// <summary>
        /// This read-only property returns the recognized words loaded from code analysis dictionaries
        /// </summary>
        public IEnumerable<string> RecognizedWords => recognizedWords;

        /// <summary>
        /// This read-only property returns the unrecognized words loaded from code analysis dictionaries
        /// </summary>
        /// <value>The key is the unrecognized word and the value is the list of spelling alternatives</value>
        public IDictionary<string, IList<string>> UnrecognizedWords => unrecognizedWords;

        /// <summary>
        /// This read-only property returns the deprecated terms loaded from code analysis dictionaries
        /// </summary>
        /// <value>The key is the deprecated term and the value is the preferred alternate</value>
        public IDictionary<string, string> DeprecatedTerms => deprecatedTerms;

        /// <summary>
        /// This read-only property returns the compound terms loaded from code analysis dictionaries
        /// </summary>
        /// <value>The key is the discrete term and the value is the compound alternate</value>
        public IDictionary<string, string> CompoundTerms => compoundTerms;

        /// <summary>
        /// This is used to indicate whether or not to spell check any WPF text box within Visual Studio
        /// </summary>
        /// <value>The default is true.  This option only applies to the global configuration.</value>
        [DefaultValue(true)]
        public bool EnableWpfTextBoxSpellChecking { get; set; }

        /// <summary>
        /// This read-only property returns an enumerable list of exclusion regular expressions that will be used
        /// to exclude WPF text boxes in Visual Studio editor and tool windows from being spell checked.
        /// </summary>
        /// <value>This option only applies to the global configuration.</value>
        public IEnumerable<Regex> VisualStudioExclusions => visualStudioExclusions;

        /// <summary>
        /// This read-only property returns the default list of ignored words
        /// </summary>
        /// <remarks>The default list includes words starting with what looks like an escape sequence such as
        /// various Doxygen documentation tags (i.e. \anchor, \ref, \remarks, etc.).</remarks>
        public static IEnumerable<string> DefaultIgnoredWords =>
            new string[] { "\\addindex", "\\addtogroup", "\\anchor", "\\arg", "\\attention", "\\author",
                "\\authors", "\\brief", "\\bug", "\\file", "\\fn", "\\name", "\\namespace", "\\nosubgrouping",
                "\\note", "\\ref", "\\refitem", "\\related", "\\relates", "\\relatedalso", "\\relatesalso",
                "\\remark", "\\remarks", "\\result", "\\return", "\\returns", "\\retval", "\\rtfonly",
                "\\tableofcontents", "\\test", "\\throw", "\\throws", "\\todo", "\\tparam", "\\typedef",
                "\\var", "\\verbatim", "\\verbinclude", "\\version", "\\vhdlflow" };

        /// <summary>
        /// This read-only property returns the default list of ignored classifications
        /// </summary>
        public static IEnumerable<KeyValuePair<string, IEnumerable<string>>> DefaultIgnoredClassifications =>
            new[] {
                // Only comments are spell checked in EditorConfig files.  Ignore the string classification
                // use by the EditorConfig Language Service extension by Mads Kristensen.
                new KeyValuePair<string, IEnumerable<string>>("EditorConfig", new[] { "string" })
            };

        /// <summary>
        /// This read-only property returns the default list of ignored file patterns
        /// </summary>
        public static IEnumerable<string> DefaultIgnoredFilePatterns =>
            new[] { @"\bin\*", "*.min.cs", "*.min.js", "*.rproj", "CodeAnalysisLog.xml", "GlobalSuppressions.*",
                "Resources.Designer.*", "Settings.Designer.cs", "Settings.settings", "UpgradeLog.htm",
                "bootstrap*.css", "bootstrap*.js", "html5shiv.js", "jquery*.d.ts", "jquery*.js", "respond*.js",
                "robots.txt" };

        /// <summary>
        /// This read-only property returns the default list of ignored XML elements
        /// </summary>
        public static IEnumerable<string> DefaultIgnoredXmlElements =>
            new string[] { "c", "code", "codeEntityReference", "codeReference", "codeInline", "command",
                "environmentVariable", "fictitiousUri", "foreignPhrase", "link", "linkTarget", "linkUri",
                "localUri", "replaceable", "resheader", "see", "seeAlso", "style", "unmanagedCodeEntityReference",
                "token" };

        /// <summary>
        /// This read-only property returns the default list of spell checked XML attributes
        /// </summary>
        public static IEnumerable<string> DefaultSpellCheckedAttributes =>
            new[] { "altText", "Caption", "CompoundAlternate", "Content", "content", "Header", "lead",
                "PreferredAlternate", "SpellingAlternates", "title", "term", "Text", "ToolTip" };

        /// <summary>
        /// This read-only property returns the default list of excluded Visual Studio text box IDs
        /// </summary>
        public static IEnumerable<string> DefaultVisualStudioExclusions => new[] {
            @".*?\.(Placement\.PART_SearchBox|Placement\.PART_EditableTextBox|ServerNameTextBox|" +
                "filterTextBox|searchTextBox|tboxFilter|txtSearchText)(?# Various search text boxes)",
            @"Microsoft\.VisualStudio\.Dialogs\.NewProjectDialog.*(?# New Project dialog box)",
            @"Microsoft\.VisualStudio\.Web\.Publish\.PublishUI\.PublishDialog.*(?# Website publishing dialog box)",
            @"131369f2-062d-44a2-8671-91ff31efb4f4.*?\.globalSettingsSectionView.*(?# Git global settings)",
            @"fbcae063-e2c0-4ab1-a516-996ea3dafb72.*(?# SQL Server object explorer)",
            @"1c79180c-bb93-46d2-b4d3-f22e7015a6f1\.txtFindID(?# SHFB resource item editor)",
            @"581e89c0-e423-4453-bde3-a0403d5f380d\.ucEntityReferences\.txtFindName(?# SHFB entity references)",
            @"7aad2922-72a2-42c1-a077-85f5097a8fa7\.txtFindID(?# SHFB content layout editor)",
            @"d481fb70-9bf0-4868-9d4c-5db33c6565e1\.(txtFindID|txtTokenName)(?# SHFB Token editor)",
            @"b270807c-d8c6-49eb-8ebe-8e8d566637a1\.(.*\.txtFolder|.*\.txtFile|txtHtmlHelpName|" +
                "txtWebsiteAdContent|txtCatalogProductId|txtCatalogName|txtVendorName|txtValue|" +
                "pgProps.*|txtPreBuildEvent|txtPostBuildEvent)(?# SHFB property page and form controls)",
            @"(SandcastleBuilder\.Components\.UI\.|Microsoft\.Ddue\.Tools\.UI\.|SandcastleBuilder\.PlugIns\.).*" +
                "(?# SHFB build component and plug-in configuration forms)",
            @"64debe95-07ea-48ac-8744-af87605d624a.*(?# Spell checker solution/project tool window)",
            @"837501d0-c07d-47c6-aab7-9ba4d78d0038\.pnlPages\.(txtAdditionalFolder|txtAttributeName|" +
                "txtFilePattern|txtIgnoredElement|txtIgnoredWord|txtImportSettingsFile)(?# Spell checker config editor)",
            @"fd92f3d8-cebf-47b9-bb98-674a1618f364.*(?# Spell checker interactive tool window)",
            @"VisualStudio\.SpellChecker\.Editors\.Pages\.ExclusionExpressionAddEditForm\.txtExpression" +
                "(?# Spell checker exclusion expression editor)",
            @"da95c001-7ed0-4f46-b5f0-351125ab8bda.*(?# Web publishing dialog box)",
            @"Microsoft\.VisualStudio\.Web\.Publish\.PublishUI\.AdvancedPreCompileOptionsDialog.*" +
                "(?# Web publishing compile options dialog box)"
        };
        #endregion

        #region Constructor
        //=====================================================================

        /// <summary>
        /// Constructor
        /// </summary>
        public SpellCheckerConfiguration()
        {
            this.CSharpOptions = new CSharpOptions();
            this.CadOptions = new CodeAnalysisDictionaryOptions();

            dictionaryLanguages = new List<CultureInfo>();

            this.SpellCheckAsYouType = this.IncludeInProjectSpellCheck = this.DetectDoubledWords =
                this.IgnoreWordsWithDigits = this.IgnoreWordsInAllUppercase = this.IgnoreWordsInMixedCase =
                this.IgnoreFormatSpecifiers = this.IgnoreFilenamesAndEMailAddresses =
                this.IgnoreXmlElementsInText = this.DetermineResourceFileLanguageFromName =
                this.InheritIgnoredFilePatterns = this.InheritAdditionalDictionaryFolders =
                this.InheritIgnoredWords = this.InheritExclusionExpressions = this.InheritXmlSettings =
                this.InheritIgnoredClassifications =  this.IgnoreMnemonics =
                this.EnableWpfTextBoxSpellChecking = true;

            ignoredWords = new HashSet<string>(DefaultIgnoredWords, StringComparer.OrdinalIgnoreCase);
            ignoredXmlElements = new HashSet<string>(DefaultIgnoredXmlElements);
            spellCheckedXmlAttributes = new HashSet<string>(DefaultSpellCheckedAttributes);
            recognizedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            loadedConfigFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ignoredWordsFiles = new List<(ConfigurationType ConfigType, string Filename)>();

            additionalDictionaryFolders = new List<string>();

            exclusionExpressions = new List<Regex>();
            ignoredFilePatterns = new List<Regex>(DefaultIgnoredFilePatterns.Select(p => p.RegexFromFilePattern()));
            visualStudioExclusions = new List<Regex>(DefaultVisualStudioExclusions.Select(p => new Regex(p)));

            deprecatedTerms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            compoundTerms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            unrecognizedWords = new Dictionary<string, IList<string>>(StringComparer.OrdinalIgnoreCase);

            ignoredClassifications = new Dictionary<string, HashSet<string>>();

            foreach(var kv in DefaultIgnoredClassifications)
                ignoredClassifications.Add(kv.Key, new HashSet<string>(kv.Value));
        }
        #endregion

        #region Helper methods
        //=====================================================================

        /// <summary>
        /// This is used to determine if the file should be excluded by name
        /// </summary>
        /// <param name="filename">The filename to check</param>
        /// <returns>True to exclude the file from spell checking, false to include it</returns>
        public bool ShouldExcludeFile(string filename)
        {
            return (String.IsNullOrWhiteSpace(filename) || ignoredFilePatterns.Any(p => p.IsMatch(filename)));
        }

        /// <summary>
        /// This method provides a thread-safe way to check for a globally ignored word
        /// </summary>
        /// <param name="word">The word to check</param>
        /// <returns>True if it should be ignored, false if not</returns>
        public bool ShouldIgnoreWord(string word)
        {
            if(String.IsNullOrWhiteSpace(word))
                return true;

            return ignoredWords.Contains(word);
        }

        /// <summary>
        /// This is used to get a set of ignored classifications for the given content type
        /// </summary>
        /// <param name="contentType">The content type for which to get ignored classifications</param>
        /// <returns>An enumerable list of ignored classifications or an empty set if there are none</returns>
        public IEnumerable<string> IgnoredClassificationsFor(string contentType)
        {
            if(!ignoredClassifications.TryGetValue(contentType, out HashSet<string> classifications))
                classifications = new HashSet<string>();

            return classifications;
        }
        #endregion

        #region Load configuration methods
        //=====================================================================

        /// <summary>
        /// Load the configuration from the given file
        /// </summary>
        /// <param name="filename">The configuration file to load</param>
        /// <remarks>Any properties not in the configuration file retain their current values.  If the file does
        /// not exist, the configuration will remain unchanged.</remarks>
        public void Load(string filename)
        {
            HashSet<string> tempHashSet;

            try
            {
                // We go through the motions of loading the configuration file even if it doesn't exist.  This
                // allows external files such as the default ignored words file to be loaded even if the default
                // global configuration file does not exist.
                var configuration = new SpellingConfigurationFile(filename, this);

                loadedConfigFiles.Add(filename);

                // Import settings from a user-defined location if necessary.  However, if we've seen the file
                // already, ignore it to prevent getting stuck in an endless loop.
                string importSettingsFile = configuration.ToString(PropertyNames.ImportSettingsFile);

                if(!String.IsNullOrWhiteSpace(importSettingsFile))
                {
                    if(importSettingsFile.IndexOf('%') != -1)
                        importSettingsFile = Environment.ExpandEnvironmentVariables(importSettingsFile);

                    if(!Path.IsPathRooted(importSettingsFile))
                        importSettingsFile = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filename),
                            importSettingsFile));

                    // For non-global settings files, settings in this file will override the settings in the
                    // imported file.  If this is the global settings file, the imported settings will be loaded
                    // last and will override the global settings since the global file doesn't inherit settings
                    // from anything else.
                    if(!loadedConfigFiles.Contains(importSettingsFile) && configuration.ConfigurationType != ConfigurationType.Global)
                        this.Load(importSettingsFile);
                }

                this.SpellCheckAsYouType = configuration.ToBoolean(PropertyNames.SpellCheckAsYouType);
                
                if(configuration.ConfigurationType != ConfigurationType.Global)
                {
                    // This option is always true for the global configuration
                    this.IncludeInProjectSpellCheck = configuration.ToBoolean(PropertyNames.IncludeInProjectSpellCheck);
                }
                else
                {
                    // These only apply to the global configuration
                    if(configuration.HasProperty(PropertyNames.VisualStudioIdExclusions))
                    {
                        this.EnableWpfTextBoxSpellChecking = configuration.ToBoolean(PropertyNames.EnableWpfTextBoxSpellChecking);

                        visualStudioExclusions = new List<Regex>(configuration.ToRegexes(PropertyNames.VisualStudioIdExclusions,
                            PropertyNames.VisualStudioIdExclusionItem));
                    }
                }

                this.DetectDoubledWords = configuration.ToBoolean(PropertyNames.DetectDoubledWords);
                this.IgnoreWordsWithDigits = configuration.ToBoolean(PropertyNames.IgnoreWordsWithDigits);
                this.IgnoreWordsInAllUppercase = configuration.ToBoolean(PropertyNames.IgnoreWordsInAllUppercase);
                this.IgnoreWordsInMixedCase = configuration.ToBoolean(PropertyNames.IgnoreWordsInMixedCase);
                this.IgnoreFormatSpecifiers = configuration.ToBoolean(PropertyNames.IgnoreFormatSpecifiers);
                this.IgnoreFilenamesAndEMailAddresses = configuration.ToBoolean(
                    PropertyNames.IgnoreFilenamesAndEMailAddresses);
                this.IgnoreXmlElementsInText = configuration.ToBoolean(PropertyNames.IgnoreXmlElementsInText);
                this.TreatUnderscoreAsSeparator = configuration.ToBoolean(PropertyNames.TreatUnderscoreAsSeparator);
                this.IgnoreMnemonics = configuration.ToBoolean(PropertyNames.IgnoreMnemonics);
                this.IgnoreCharacterClass = configuration.ToEnum<IgnoredCharacterClass>(
                    PropertyNames.IgnoreCharacterClass);
                this.DetermineResourceFileLanguageFromName = configuration.ToBoolean(
                    PropertyNames.DetermineResourceFileLanguageFromName);

                this.CSharpOptions.IgnoreXmlDocComments = configuration.ToBoolean(
                    PropertyNames.CSharpOptionsIgnoreXmlDocComments);
                this.CSharpOptions.IgnoreDelimitedComments = configuration.ToBoolean(
                    PropertyNames.CSharpOptionsIgnoreDelimitedComments);
                this.CSharpOptions.IgnoreStandardSingleLineComments = configuration.ToBoolean(
                    PropertyNames.CSharpOptionsIgnoreStandardSingleLineComments);
                this.CSharpOptions.IgnoreQuadrupleSlashComments = configuration.ToBoolean(
                    PropertyNames.CSharpOptionsIgnoreQuadrupleSlashComments);
                this.CSharpOptions.IgnoreNormalStrings = configuration.ToBoolean(
                    PropertyNames.CSharpOptionsIgnoreNormalStrings);
                this.CSharpOptions.IgnoreVerbatimStrings = configuration.ToBoolean(
                    PropertyNames.CSharpOptionsIgnoreVerbatimStrings);
                this.CSharpOptions.IgnoreInterpolatedStrings = configuration.ToBoolean(
                    PropertyNames.CSharpOptionsIgnoreInterpolatedStrings);
                this.CSharpOptions.ApplyToAllCStyleLanguages = configuration.ToBoolean(
                    PropertyNames.CSharpOptionsApplyToAllCStyleLanguages);

                this.CadOptions.ImportCodeAnalysisDictionaries = configuration.ToBoolean(
                    PropertyNames.CadOptionsImportCodeAnalysisDictionaries);
                this.CadOptions.RecognizedWordHandling = configuration.ToEnum<RecognizedWordHandling>(
                    PropertyNames.CadOptionsRecognizedWordHandling);
                this.CadOptions.TreatUnrecognizedWordsAsMisspelled = configuration.ToBoolean(
                    PropertyNames.CadOptionsTreatUnrecognizedWordsAsMisspelled);
                this.CadOptions.TreatDeprecatedTermsAsMisspelled = configuration.ToBoolean(
                    PropertyNames.CadOptionsTreatDeprecatedTermsAsMisspelled);
                this.CadOptions.TreatCompoundTermsAsMisspelled = configuration.ToBoolean(
                    PropertyNames.CadOptionsTreatCompoundTermsAsMisspelled);
                this.CadOptions.TreatCasingExceptionsAsIgnoredWords = configuration.ToBoolean(
                    PropertyNames.CadOptionsTreatCasingExceptionsAsIgnoredWords);

                this.InheritAdditionalDictionaryFolders = configuration.ToBoolean(
                    PropertyNames.InheritAdditionalDictionaryFolders);

                if(configuration.HasProperty(PropertyNames.AdditionalDictionaryFolders))
                {
                    tempHashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach(string folder in configuration.ToValues(PropertyNames.AdditionalDictionaryFolders,
                      PropertyNames.AdditionalDictionaryFoldersItem))
                    {
                        // Fully qualify relative paths with the configuration file path
                        if(folder.IndexOf('%') != -1 || Path.IsPathRooted(folder))
                            tempHashSet.Add(folder);
                        else
                            tempHashSet.Add(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filename), folder)));
                    }

                    if(this.InheritAdditionalDictionaryFolders)
                    {
                        if(tempHashSet.Count != 0)
                            foreach(string folder in tempHashSet)
                                additionalDictionaryFolders.Add(folder);
                    }
                    else
                        additionalDictionaryFolders = tempHashSet.ToList();
                }

                this.InheritIgnoredWords = configuration.ToBoolean(PropertyNames.InheritIgnoredWords);

                if(configuration.HasProperty(PropertyNames.IgnoredWords))
                {
                    tempHashSet = new HashSet<string>(configuration.ToValues(PropertyNames.IgnoredWords,
                        PropertyNames.IgnoredWordsItem), StringComparer.OrdinalIgnoreCase);

                    // For global configurations, we always want to replace the default set
                    if(this.InheritIgnoredWords && configuration.ConfigurationType != ConfigurationType.Global)
                    {
                        if(tempHashSet.Count != 0)
                            foreach(string word in tempHashSet)
                                ignoredWords.Add(word);
                    }
                    else
                        ignoredWords = tempHashSet;
                }

                // Load the ignored words file if one is specified.  The global ignored words file is always
                // added even if not specified or it doesn't exist yet.
                string ignoredWordsFile = configuration.ToString(PropertyNames.IgnoredWordsFile);

                if(String.IsNullOrWhiteSpace(ignoredWordsFile) && configuration.ConfigurationType == ConfigurationType.Global)
                    ignoredWordsFile = "IgnoredWords.dic";

                if(!String.IsNullOrWhiteSpace(ignoredWordsFile))
                {
                    if(ignoredWordsFile.IndexOf('%') != -1)
                        ignoredWordsFile = Environment.ExpandEnvironmentVariables(ignoredWordsFile);

                    if(!Path.IsPathRooted(ignoredWordsFile))
                    {
                        ignoredWordsFile = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(configuration.Filename),
                            ignoredWordsFile));
                    }

                    if(File.Exists(ignoredWordsFile))
                        ignoredWords.UnionWith(Utility.LoadUserDictionary(ignoredWordsFile, false, false));

                    if(!ignoredWordsFiles.Any(f => f.Filename.Equals(ignoredWordsFile, StringComparison.OrdinalIgnoreCase)))
                        ignoredWordsFiles.Add((configuration.ConfigurationType, ignoredWordsFile));
                }

                this.InheritExclusionExpressions = configuration.ToBoolean(PropertyNames.InheritExclusionExpressions);

                if(configuration.HasProperty(PropertyNames.ExclusionExpressions))
                {
                    var tempList = new List<Regex>(configuration.ToRegexes(PropertyNames.ExclusionExpressions,
                        PropertyNames.ExclusionExpressionItem));

                    if(this.InheritExclusionExpressions)
                    {
                        if(tempList.Count != 0)
                        {
                            tempHashSet = new HashSet<string>(exclusionExpressions.Select(r => r.ToString()));

                            foreach(Regex exp in tempList)
                                if(!tempHashSet.Contains(exp.ToString()))
                                {
                                    exclusionExpressions.Add(exp);
                                    tempHashSet.Add(exp.ToString());
                                }
                        }
                    }
                    else
                        exclusionExpressions = tempList;
                }

                // Always add the Ignore Spelling directive expression as we don't want the directive words
                // included when spell checking with non-English dictionaries.
                string directiveExp = ProjectSpellCheck.InlineIgnoredWord.reIgnoreSpelling.ToString();

                if(!exclusionExpressions.Any(e => e.ToString().Equals(directiveExp, StringComparison.Ordinal)))
                    exclusionExpressions.Add(new Regex(directiveExp,
                        ProjectSpellCheck.InlineIgnoredWord.reIgnoreSpelling.Options));

                this.InheritIgnoredFilePatterns = configuration.ToBoolean(PropertyNames.InheritIgnoredFilePatterns);

                if(configuration.HasProperty(PropertyNames.IgnoredFilePatterns))
                {
                    var tempList = new List<string>(configuration.ToValues(PropertyNames.IgnoredFilePatterns,
                        PropertyNames.IgnoredFilePatternItem));

                    // For global configurations, we always want to replace the default set
                    if(!this.InheritIgnoredFilePatterns || configuration.ConfigurationType == ConfigurationType.Global)
                        ignoredFilePatterns.Clear();

                    if(tempList.Count != 0)
                    {
                        tempHashSet = new HashSet<string>(ignoredFilePatterns.Select(r => r.ToString()));

                        foreach(string exp in tempList)
                        {
                            Regex pattern = exp.RegexFromFilePattern();

                            if(!tempHashSet.Contains(pattern.ToString()))
                            {
                                ignoredFilePatterns.Add(pattern);
                                tempHashSet.Add(pattern.ToString());
                            }
                        }
                    }
                }

                this.InheritXmlSettings = configuration.ToBoolean(PropertyNames.InheritXmlSettings);

                if(configuration.HasProperty(PropertyNames.IgnoredXmlElements))
                {
                    tempHashSet = new HashSet<string>(configuration.ToValues(PropertyNames.IgnoredXmlElements,
                        PropertyNames.IgnoredXmlElementsItem));

                    // For global configurations, we always want to replace the default set
                    if(this.InheritXmlSettings && configuration.ConfigurationType != ConfigurationType.Global)
                    {
                        if(tempHashSet.Count != 0)
                            foreach(string element in tempHashSet)
                                ignoredXmlElements.Add(element);
                    }
                    else
                        ignoredXmlElements = tempHashSet;
                }

                if(configuration.HasProperty(PropertyNames.SpellCheckedXmlAttributes))
                {
                    tempHashSet = new HashSet<string>(configuration.ToValues(PropertyNames.SpellCheckedXmlAttributes,
                        PropertyNames.SpellCheckedXmlAttributesItem));

                    // For global configurations, we always want to replace the default set
                    if(this.InheritXmlSettings && configuration.ConfigurationType != ConfigurationType.Global)
                    {
                        if(tempHashSet.Count != 0)
                            foreach(string attr in tempHashSet)
                                spellCheckedXmlAttributes.Add(attr);
                    }
                    else
                        spellCheckedXmlAttributes = tempHashSet;
                }

                this.InheritIgnoredClassifications = configuration.ToBoolean(PropertyNames.InheritIgnoredClassifications);

                if(configuration.HasProperty(PropertyNames.IgnoredClassifications))
                {
                    // For global configurations, we always want to replace the default set
                    if(!this.InheritIgnoredClassifications || configuration.ConfigurationType == ConfigurationType.Global)
                        ignoredClassifications.Clear();

                    foreach(var type in configuration.Element(PropertyNames.IgnoredClassifications).Elements(
                      PropertyNames.ContentType))
                    {
                        string typeName = type.Attribute(PropertyNames.ContentTypeName).Value;

                        if(!ignoredClassifications.TryGetValue(typeName, out HashSet<string> classifications))
                        {
                            classifications = new HashSet<string>();
                            ignoredClassifications.Add(typeName, classifications);
                        }

                        foreach(var c in type.Elements(PropertyNames.Classification))
                            classifications.Add(c.Value);
                    }
                }

                // Load the dictionary languages and, if merging settings, handle inheritance
                if(configuration.HasProperty(PropertyNames.SelectedLanguages))
                {
                    var languages = configuration.ToValues(PropertyNames.SelectedLanguages,
                      PropertyNames.SelectedLanguagesItem, true).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                    // Is there a blank entry that marks the inherited languages placeholder?
                    int idx = languages.IndexOf(String.Empty);

                    if(idx != -1)
                    {
                        languages.RemoveAt(idx);

                        // If there are other languages, insert the inherited languages at the desired location.
                        // If an inherited language matches a language in the configuration file, it is left at
                        // its new location this overriding the inherited language location.
                        if(languages.Count != 0)
                            foreach(var lang in dictionaryLanguages)
                                if(!languages.Contains(lang.Name))
                                {
                                    languages.Insert(idx, lang.Name);
                                    idx++;
                                }
                    }

                    if(languages.Count != 0)
                        dictionaryLanguages = languages.Select(l => new CultureInfo(l)).ToList();
                }

                // As noted above, imported settings override settings in the global configuration
                if(!String.IsNullOrWhiteSpace(importSettingsFile) && !loadedConfigFiles.Contains(importSettingsFile) &&
                  configuration.ConfigurationType == ConfigurationType.Global)
                {
                    this.Load(importSettingsFile);
                }
            }
            catch(Exception ex)
            {
                // Ignore errors and just use the defaults
                System.Diagnostics.Debug.WriteLine(ex);
            }
            finally
            {
                // Always ensure we have at least the default language if none are specified in the global
                // configuration file.
                if(dictionaryLanguages.Count == 0)
                    dictionaryLanguages.Add(new CultureInfo("en-US"));
            }
        }

        /// <summary>
        /// This is used to import spelling words and ignored words from a code analysis dictionary file
        /// </summary>
        /// <param name="filename">The code analysis dictionary file to import</param>
        public void ImportCodeAnalysisDictionary(string filename)
        {
            XDocument settings = XDocument.Load(filename);
            XElement root = settings.Root, option;

            option = root.XPathSelectElement("Words/Recognized");

            if(this.CadOptions.RecognizedWordHandling != RecognizedWordHandling.None && option != null)
                foreach(var word in option.Elements("Word"))
                    if(!String.IsNullOrWhiteSpace(word.Value))
                        switch(this.CadOptions.RecognizedWordHandling)
                        {
                            case RecognizedWordHandling.IgnoreAllWords:
                                ignoredWords.Add(word.Value);
                                break;

                            case RecognizedWordHandling.AddAllWords:
                                recognizedWords.Add(word.Value);
                                break;

                            default:    // Attribute determines usage
                                if((string)word.Attribute("Spelling") == "Add")
                                    recognizedWords.Add(word.Value);
                                else
                                    if((string)word.Attribute("Spelling") == "Ignore")
                                        ignoredWords.Add(word.Value);

                                // Any other value is treated as None and it passes through to the spell checker
                                // like any other word.
                                break;
                        }

            option = root.XPathSelectElement("Words/Unrecognized");

            if(this.CadOptions.TreatUnrecognizedWordsAsMisspelled && option != null)
                foreach(var word in option.Elements("Word"))
                    if(!String.IsNullOrWhiteSpace(word.Value))
                    {
                        unrecognizedWords[word.Value] = new List<string>(
                            ((string)word.Attribute("SpellingAlternates") ?? String.Empty).Split(
                                new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries));
                    }

            option = root.XPathSelectElement("Words/Deprecated");

            if(this.CadOptions.TreatDeprecatedTermsAsMisspelled && option != null)
                foreach(var term in option.Elements("Term"))
                    if(!String.IsNullOrWhiteSpace(term.Value))
                        deprecatedTerms[term.Value] = ((string)term.Attribute("PreferredAlternate")).ToWords();

            option = root.XPathSelectElement("Words/Compound");

            if(this.CadOptions.TreatCompoundTermsAsMisspelled && option != null)
                foreach(var term in option.Elements("Term"))
                    if(!String.IsNullOrWhiteSpace(term.Value))
                        compoundTerms[term.Value] = ((string)term.Attribute("CompoundAlternate")).ToWords();

            option = root.XPathSelectElement("Acronyms/CasingExceptions");

            if(this.CadOptions.TreatCasingExceptionsAsIgnoredWords && option != null)
                foreach(var acronym in option.Elements("Acronym"))
                    if(!String.IsNullOrWhiteSpace(acronym.Value))
                        ignoredWords.Add(acronym.Value);
        }
        #endregion
    }
}
