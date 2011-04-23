﻿#region License
/* 
ClipboardDiff Visual Studio Extension
Copyright (C) 2011 Einar Egilsson
http://tech.einaregilsson.com/2011/xx/xx/clipboard-diff/

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/
#endregion
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.ComponentModel.Design;
using System.Windows.Forms;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Process = System.Diagnostics.Process;

namespace EinarEgilsson.ClipboardDiff
{
    [PackageRegistration(UseManagedResourcesOnly = true)]
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid("b02989c2-1a8e-4f11-81a4-957f1d18db10")]
    [ProvideAutoLoad(UIContextGuids80.NoSolution)]
    [ProvideAutoLoad(UIContextGuids80.SolutionExists)]
    public sealed class ClipboardDiffPackage : Package
    {
        public static readonly Guid CommandSetId = new Guid("6f04d587-0360-458b-8501-02b2bc7bb002");
        public const int ShowSettingsWindowCommandId = 0x100;
        public const int ClipboardDiffCommandId = 0x120;

        private const string RegistryRoot = "ClipboardDiff";
        private const string RegistryProgram = "DiffProgram";
        private const string RegistryArguments = "Arguments";

        private string _program;
        private string _arguments;
        private DTE2 _app;
        private readonly string tempFolder = Path.Combine(Path.GetTempPath(), "ClipboardDiff");


        private void SaveSettings()
        {
            
            var key = UserRegistryRoot.OpenSubKey(RegistryRoot, true);
            if (key == null)
            {
                UserRegistryRoot.CreateSubKey(RegistryRoot);
                key = UserRegistryRoot.OpenSubKey(RegistryRoot, true);
            }
            Debug.Assert(key != null);
            key.SetValue(RegistryProgram, _program);
            key.SetValue(RegistryArguments, _arguments);
        }

        private void LoadSettings()
        {
            var subKey = UserRegistryRoot.OpenSubKey(RegistryRoot);
            if (subKey != null)
            {
                _program = (string)subKey.GetValue(RegistryProgram);
                _arguments = (string)subKey.GetValue(RegistryArguments);
            }

            //Didn't exist, try default paths
            if (string.IsNullOrEmpty(_program))
            {
                foreach (var pair in DiffTools.Paths)
                {
                    if (File.Exists(pair.Key))
                    {
                        _program = pair.Key;
                        _arguments = pair.Value;
                        return;
                    }
                }
            }
        }

        protected override void Initialize()
        {
            base.Initialize();
            _app = (DTE2)GetGlobalService(typeof(DTE));
            InitializeMenuCommands();
            LoadSettings();
        }

        private void InitializeMenuCommands()
        {
            var mcs = GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (mcs != null)
            {
                mcs.AddCommand(new MenuCommand((o,e)=>ShowSettingsWindow(), new CommandID(CommandSetId, ShowSettingsWindowCommandId)));
                var diffCommand = new OleMenuCommand((o,e)=>DiffWithClipboard(), new CommandID(CommandSetId, ClipboardDiffCommandId));
                diffCommand.BeforeQueryStatus += (cmd, e) => ((MenuCommand) cmd).Enabled = ClipboardAndSelectionBothHaveText();
                mcs.AddCommand(diffCommand);
            }
        }

        private bool ClipboardAndSelectionBothHaveText()
        {
            return Clipboard.ContainsText()
                && _app.ActiveDocument != null
                && _app.ActiveDocument.Selection is TextSelection
                && ((TextSelection)_app.ActiveDocument.Selection).Text.Length > 0;
        }


        private void DiffWithClipboard()
        {
            if (!ClipboardAndSelectionBothHaveText())
            {
                return;
            }

            if (!VerifySettings())
            {
                return;
            }
            
            VerifyTempFolder();

            string extension = Path.GetExtension(_app.ActiveDocument.Name);

            string clipboardFile = WriteClipboardTextToTempFile(extension);
            string selectionFile = WriteSelectionTextToTempFile(extension);

            StartDiffProgram(clipboardFile, selectionFile);
            DeleteOldTempFiles(clipboardFile, selectionFile);
        }

        private string WriteClipboardTextToTempFile(string extension)
        {
            string clipboardFile = Path.Combine(tempFolder, "clipboard_" + DateTime.Now.Ticks + extension);
            File.WriteAllText(clipboardFile, Clipboard.GetText());
            return clipboardFile;
        }

        private string WriteSelectionTextToTempFile(string extension)
        {
            string selectionFile = Path.Combine(tempFolder, "selection_" + DateTime.Now.Ticks + extension);
            File.WriteAllText(selectionFile, ((TextSelection)_app.ActiveDocument.Selection).Text);
            return selectionFile;
        }

        private void StartDiffProgram(string clipboardFile, string selectionFile)
        {
            Process.Start(_program, GetArguments(clipboardFile, selectionFile));
        }

        private string GetArguments(string clipboardFile, string selectionFile)
        {
            string args = _arguments.Replace("$FILE1$", "\"" + clipboardFile + "\"").Replace("$FILE2$",
                                                                                             "\"" + selectionFile + "\"");
            args = args.Replace("\"\"", "\"");
            return args;
        }

        private bool VerifySettings()
        {
            if (string.IsNullOrEmpty(_program))
            {
                MessageBox.Show(
                    "You have not used Clipboard Diff before. You must first choose which diff tool you want to use.",
                    "Diff tool missing", MessageBoxButtons.OK, MessageBoxIcon.Information);
                ShowSettingsWindow();
            }

            //True if we have the settings set up, so caller can continue with diff operation
            return !string.IsNullOrEmpty(_program);
        }

        private void DeleteOldTempFiles(string clipboardFile, string selectionFile)
        {
            foreach (string filename in Directory.GetFiles(tempFolder))
            {
                //Clean older files that are still hanging around
                if (filename != selectionFile && filename != clipboardFile)
                {
                    try
                    {
                        File.Delete(filename);
                    }
// ReSharper disable EmptyGeneralCatchClause
                    catch
// ReSharper restore EmptyGeneralCatchClause
                    {
                        //Do nothing! We just try
                    }
                }
            }
        }

        private void VerifyTempFolder()
        {
            if (!Directory.Exists(tempFolder))
            {
                Directory.CreateDirectory(tempFolder);
            }
        }


        private void ShowSettingsWindow()
        {
            var dlg = new Settings(_program ?? "", _arguments ?? "$FILE1$ $FILE2$");
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _program = dlg.Program;
                _arguments = dlg.Arguments;
                SaveSettings();
            }
        }
    }
}
