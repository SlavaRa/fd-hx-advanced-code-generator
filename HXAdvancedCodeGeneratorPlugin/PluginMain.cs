﻿using ASCompletion;
using ASCompletion.Completion;
using ASCompletion.Context;
using ASCompletion.Model;
using PluginCore;
using PluginCore.Controls;
using PluginCore.Helpers;
using PluginCore.Localization;
using PluginCore.Managers;
using PluginCore.Utilities;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;

namespace HXCodeGenerator
{
    public class PluginMain : IPlugin
    {
        private string pluginName = "HXCodeGenerator";
        private string pluginGuid = "92f41ee5-6d96-4f03-95a5-b46610fe5c2e";
        private string pluginHelp = "www.flashdevelop.org/community/";
        private string pluginDesc = "Haxe advanced code generator for the ASCompletion engine.";
        private string pluginAuth = "FlashDevelop Team";
        private Settings settingObject;
        private string settingFilename;

        private static Regex reModifiers = new Regex("^\\s*(\\$\\(Boundary\\))?([a-z ]+)(function|var)", RegexOptions.Compiled);
        private static Regex reModifier = new Regex("(public |private )", RegexOptions.Compiled);
        private static Regex reMember = new Regex("(class |var |function )", RegexOptions.Compiled);
        
        #region Required Properties

        /// <summary>
        /// Api level of the plugin
        /// </summary>
        public int Api { get { return 1; } }

        /// <summary>
        /// Name of the plugin
        /// </summary>
        public string Name { get { return pluginName; } }

        /// <summary>
        /// GUID of the plugin
        /// </summary>
        public string Guid { get { return pluginGuid; } }

        /// <summary>
        /// Author of the plugin
        /// </summary>
        public string Author { get { return pluginAuth; } }

        /// <summary>
        /// Description of the plugin
        /// </summary>
        public string Description { get { return pluginDesc; } }

        /// <summary>
        /// Web address for help
        /// </summary>
        public string Help { get { return pluginHelp; } }

        /// <summary>
        /// Object that contains the settings
        /// </summary>
        [Browsable(false)]
        public object Settings { get { return settingObject; } }

        #endregion

        #region Required Methods

        /// <summary>
        /// Initializes the plugin
        /// </summary>
        public void Initialize()
        {
            InitBasics();
            LoadSettings();
            AddEventHandlers();
        }

        /// <summary>
        /// Disposes the plugin
        /// </summary>
        public void Dispose()
        {
            SaveSettings();
        }

        /// <summary>
        /// Handles the incoming events
        /// </summary>
        public void HandleEvent(object sender, NotifyEvent e, HandlingPriority prority)
        {
            switch (e.Type)
            {
                case EventType.Command:
                    DataEvent de = (DataEvent)e;
                    switch (de.Action)
                    {
                        case "ASCompletion.ContextualGenerator":
                            e.Handled = ASContext.HasContext && ASContext.Context.IsFileValid && ContextualGenerator(ASContext.CurSciControl);
                            break;
                    }
                    break;
            }
        }

        #endregion

        #region Custom Methods

        /// <summary>
        /// Initializes important variables
        /// </summary>
        public void InitBasics()
        {
            string dataPath = Path.Combine(PathHelper.DataDir, "HXCodeGenerator");
            if (!Directory.Exists(dataPath)) Directory.CreateDirectory(dataPath);
            this.settingFilename = Path.Combine(dataPath, "Settings.fdb");
            this.pluginDesc = TextHelper.GetString("Info.Description");
        }

        /// <summary>
        /// Adds the required event handlers
        /// </summary>
        public void AddEventHandlers()
        {
            EventManager.AddEventHandler(this, EventType.UIStarted | EventType.Command);
        }

        /// <summary>
        /// Loads the plugin settings
        /// </summary>
        public void LoadSettings()
        {
            settingObject = new Settings();
            if (!File.Exists(settingFilename)) SaveSettings();
            else settingObject = (Settings)ObjectSerializer.Deserialize(settingFilename, settingObject);
        }

        /// <summary>
        /// Saves the plugin settings
        /// </summary>
        private void SaveSettings()
        {
            ObjectSerializer.Serialize(settingFilename, settingObject);
        }

        #endregion
        
        public static void GenerateJob(GeneratorJobType job, MemberModel member, ClassModel inClass, string itemLabel, object data)
        {
            if (!GetLangIsValid()) return;
            ScintillaNet.ScintillaControl Sci = ASContext.CurSciControl;
            Sci.BeginUndoAction();
            try
            {
                switch (job)
                {
                    case GeneratorJobType.ChangeAccess:
                        ChangeAccess(Sci, member);
                        if (ASContext.CommonSettings.StartWithModifiers) FixModifiersLocation(Sci, member);
                        FixInlineModifierLocation(Sci, member);
                        FixFinalModifierLocation(Sci, member);
                        FixNoCompletionMetaLocation(Sci, member);
                        break;
                    case GeneratorJobType.MakeClassFinal:
                    case GeneratorJobType.MakeMethodFinal:
                        MemberModel m = inClass ?? member;
                        AddModifier(Sci, m, "@:final ");
                        FixFinalModifierLocation(Sci, m);
                        break;
                    case GeneratorJobType.MakeClassNotFinal:
                    case GeneratorJobType.MakeMethodNotFinal:
                        RemoveModifier(Sci, inClass ?? member, "@:final\\s");
                        break;
                    case GeneratorJobType.MakeClassExtern:
                        AddModifier(Sci, inClass, "extern ");
                        break;
                    case GeneratorJobType.MakeClassNotExtern:
                        RemoveModifier(Sci, inClass, "extern\\s");
                        break;
                    case GeneratorJobType.AddStaticModifier:
                        if ((member.Flags & FlagType.Function) > 0) RemoveModifier(Sci, member, "@:final\\s");
                        AddModifier(Sci, member, "static ");
                        if (ASContext.CommonSettings.StartWithModifiers) FixModifiersLocation(Sci, member);
                        FixInlineModifierLocation(Sci, member);
                        break;
                    case GeneratorJobType.RemoveStaticModifier:
                        RemoveModifier(Sci, member, "static\\s");
                        break;
                    case GeneratorJobType.AddInlineModifier:
                        AddModifier(Sci, member, "inline ");
                        FixInlineModifierLocation(Sci, member);
                        break;
                    case GeneratorJobType.RemoveInlineModifier:
                        RemoveModifier(Sci, member, "inline\\s");
                        break;
                    case GeneratorJobType.AddNoCompletionMeta:
                        AddModifier(Sci, member, "@:noCompletion ");
                        FixNoCompletionMetaLocation(Sci, member);
                        break;
                    case GeneratorJobType.RemoveNoCompletionMeta:
                        RemoveModifier(Sci, member, "@:noCompletion\\s");
                        break;
                }
            }
            finally
            {
                Sci.EndUndoAction();
            }
        }

        private static bool ContextualGenerator(ScintillaNet.ScintillaControl Sci)
        {
            int position = Sci.CurrentPos;
            int line = Sci.LineFromPosition(position);
            string text = Sci.GetLine(line);
            FoundDeclaration found = GetDeclarationAtLine(Sci, line);
            if (!GetDeclarationIsValid(Sci, found)) return false;
            if (found.member == null && found.inClass != ClassModel.VoidClass)
            {
                ShowChangeClass(found);
                return true;
            }
            FlagType flags = found.member.Flags;
            if ((flags & FlagType.Constructor) > 0)
            {
                ShowChangeConstructor(found);
                return true;
            }
            if ((flags & FlagType.Function) > 0)
            {
                ShowChangeMethod(found);
                return true;
            }
            if ((flags & FlagType.LocalVar) == 0 && (flags & (FlagType.Variable | FlagType.Getter | FlagType.Setter)) > 0)
            {
                ShowChangeVariable(found);
                return true;
            }
            return false;
        }

        private static FoundDeclaration GetDeclarationAtLine(ScintillaNet.ScintillaControl Sci, int line)
        {
            FoundDeclaration result = new FoundDeclaration();
            foreach (ClassModel aClass in ASContext.Context.CurrentModel.Classes)
            {
                if (aClass.LineFrom > line || aClass.LineTo < line) continue;
                result.inClass = aClass;
                foreach (MemberModel member in aClass.Members)
                {
                    if (member.LineFrom > line || member.LineTo < line) continue;
                    result.member = member;
                    return result;
                }
                return result;
            }
            return result;
        }

        private static bool GetLangIsValid()
        {
            IProject project = PluginBase.CurrentProject;
            if (project == null) return false;
            return project.Language.StartsWith("haxe");
        }
        
        private static bool GetDeclarationIsValid(ScintillaNet.ScintillaControl Sci, FoundDeclaration found)
        {
            if (found.GetIsEmpty()) return false;
            if (found.member != null) return GetCurrentPosIsValid(Sci, found.member);
            return GetCurrentPosIsValid(Sci, found.inClass);
        }

        private static bool GetCurrentPosIsValid(ScintillaNet.ScintillaControl Sci, MemberModel member)
        {
            for (int line = member.LineFrom; line <= member.LineTo; line++)
            {
                string text = Sci.GetLine(line);
                if (string.IsNullOrEmpty(text)) continue;
                Match m = reMember.Match(text);
                if (!m.Success) continue;
                int curPos = Sci.CurrentPos;
                return (Sci.PositionFromLine(line) + m.Index + m.Length) > curPos || Sci.LineEndPosition(line) == curPos;
            }
            return false;
        }

        private static void ShowChangeClass(FoundDeclaration found)
        {
            List<ICompletionListItem> known = new List<ICompletionListItem>();
            ClassModel inClass = found.inClass;
            FlagType flags = found.inClass.Flags;
            bool isFinal = (flags & FlagType.Final) > 0;
            bool isExtern = (flags & FlagType.Extern) > 0;
            if (!isFinal)
            {
                string label = "Make final";//TODO: localize it
                known.Add(new GeneratorItem(label, GeneratorJobType.MakeClassFinal, null, inClass));
            }
            if (!isExtern)
            {
                string label = "Make extern";//TODO: localize it
                known.Add(new GeneratorItem(label, GeneratorJobType.MakeClassExtern, null, inClass));
            }
            if (isFinal)
            {
                string label = "Make not final";//TODO: localize it
                known.Add(new GeneratorItem(label, GeneratorJobType.MakeClassNotFinal, null, inClass));
            }
            if (isExtern)
            {
                string label = "Make not extern";//TODO: localize it
                known.Add(new GeneratorItem(label, GeneratorJobType.MakeClassNotExtern, null, inClass));
            }
            CompletionList.Show(known, false);
        }

        private static void ShowChangeConstructor(FoundDeclaration found)
        {
            List<ICompletionListItem> known = new List<ICompletionListItem>();
            MemberModel member = found.member;
            FlagType flags = member.Flags;
            bool isPrivate = (member.Access & Visibility.Private) > 0;
            if (isPrivate)
            {
                string label = "Make public";//TODO: localize it
                known.Add(new GeneratorItem(label, GeneratorJobType.ChangeAccess, member, null));
            }
            else
            {
                string label = "Make private";//TODO: localize it
                known.Add(new GeneratorItem(label, GeneratorJobType.ChangeAccess, member, null));
            }
            CompletionList.Show(known, false);
        }

        private static void ShowChangeMethod(FoundDeclaration found)
        {
            List<ICompletionListItem> known = new List<ICompletionListItem>();
            MemberModel member = found.member;
            FlagType flags = member.Flags;
            bool isPrivate = (member.Access & Visibility.Private) > 0;
            bool isStatic = (flags & FlagType.Static) > 0;
            bool isFinal = (flags & FlagType.Final) > 0;
            ScintillaNet.ScintillaControl Sci = ASContext.CurSciControl;
            bool isInline = GetHasModifier(Sci, member, "inline\\s");
            bool isNoCompletion = GetHasModifier(Sci, member, "@:noCompletion\\s");
            if (isPrivate)
            {
                string label = "Make public";//TODO: localize it
                known.Add(new GeneratorItem(label, GeneratorJobType.ChangeAccess, member, null));
            }
            else
            {
                string label = "Make private";//TODO: localize it
                known.Add(new GeneratorItem(label, GeneratorJobType.ChangeAccess, member, null));
            }
            if (!isStatic && !isFinal)
            { 
                string label = "Make final";//TODO: localize it
                known.Add(new GeneratorItem(label, GeneratorJobType.MakeMethodFinal, member, null));
            }
            if (!isStatic)
            {
                string label = "Add static modifier";//TODO: localize it
                known.Add(new GeneratorItem(label, GeneratorJobType.AddStaticModifier, member, null));
            }
            if (!isInline)
            {
                string label = "Add inline modifier";//TODO: localize it
                known.Add(new GeneratorItem(label, GeneratorJobType.AddInlineModifier, member, null));
            }
            if (!isNoCompletion)
            {
                string label = "Add @:noCompletion";//TODO: localize it
                known.Add(new GeneratorItem(label, GeneratorJobType.AddNoCompletionMeta, member, null));
            }
            if (!isStatic && isFinal)
            {
                string label = "Make not final";//TODO: localize it
                known.Add(new GeneratorItem(label, GeneratorJobType.MakeMethodNotFinal, member, null));
            }
            if (isStatic)
            {
                string label = "Remove static modifier";//TODO: localize it
                known.Add(new GeneratorItem(label, GeneratorJobType.RemoveStaticModifier, member, null));
            }
            if (isInline)
            {
                string label = "Remove inline modifier";//TODO: localize it
                known.Add(new GeneratorItem(label, GeneratorJobType.RemoveInlineModifier, member, null));
            }
            if (isNoCompletion)
            {
                string label = "Remove @:noCompletion";//TODO: localize it
                known.Add(new GeneratorItem(label, GeneratorJobType.RemoveNoCompletionMeta, member, null));
            }
            CompletionList.Show(known, false);
        }

        private static void ShowChangeVariable(FoundDeclaration found)
        {
            List<ICompletionListItem> known = new List<ICompletionListItem>();
            MemberModel member = found.member;
            FlagType flags = member.Flags;
            ScintillaNet.ScintillaControl Sci = ASContext.CurSciControl;
            bool isPrivate = (member.Access & Visibility.Private) > 0;
            bool isStatic = (flags & FlagType.Static) > 0;
            bool isNoCompletion = GetHasModifier(Sci, member, "@:noCompletion\\s");
            if (isPrivate)
            {
                string label = "Make public";//TODO: localize it
                known.Add(new GeneratorItem(label, GeneratorJobType.ChangeAccess, member, null));
            }
            else
            {
                string label = "Make private";//TODO: localize it
                known.Add(new GeneratorItem(label, GeneratorJobType.ChangeAccess, member, null));
            }
            if (!isStatic)
            {
                string label = "Add static modifier";//TODO: localize it
                known.Add(new GeneratorItem(label, GeneratorJobType.AddStaticModifier, member, null));
            }
            if (!isNoCompletion)
            {
                string label = "Add @:noCompletion";//TODO: localize it
                known.Add(new GeneratorItem(label, GeneratorJobType.AddNoCompletionMeta, member, null));
            }
            if (isStatic)
            {
                string label = "Remove static modifier";//TODO: localize it
                known.Add(new GeneratorItem(label, GeneratorJobType.RemoveStaticModifier, member, null));
            }
            if (isNoCompletion)
            {
                string label = "Remove @:noCompletion";//TODO: localize it
                known.Add(new GeneratorItem(label, GeneratorJobType.RemoveNoCompletionMeta, member, null));
            }
            CompletionList.Show(known, false);
        }

        private static bool GetHasModifier(ScintillaNet.ScintillaControl Sci, MemberModel member, string modifier)
        {
            for (int line = member.LineFrom; line <= member.LineTo; line++)
            {
                string text = Sci.GetLine(line);
                if (!string.IsNullOrEmpty(text) && Regex.IsMatch(text, modifier)) return true;
            }
            return false;
        }

        private static void ChangeAccess(ScintillaNet.ScintillaControl Sci, MemberModel member)
        {
            for (int line = member.LineFrom; line <= member.LineTo; line++)
            {
                string text = Sci.GetLine(line);
                if (string.IsNullOrEmpty(text)) continue;
                Match m = reMember.Match(text);
                if (!m.Success) continue;
                int start = Sci.PositionFromLine(line);
                Sci.SetSel(start, start + text.Length);
                string access = (member.Access & Visibility.Private) > 0 ? "public " : "private ";
                m = reModifier.Match(text);
                if (m.Success) text = text.Remove(m.Index, m.Length).Insert(m.Index, access);
                else
                {
                    m = Regex.Match(text, "[@:a-z ]", RegexOptions.IgnoreCase);
                    text = text.Insert(m.Index, access);
                }
                Sci.ReplaceSel(text);
                return;
            }
        }

        private static void AddModifier(ScintillaNet.ScintillaControl Sci, MemberModel member, string modifier)
        {
            for (int line = member.LineFrom; line <= member.LineTo; line++)
            {
                string text = Sci.GetLine(line);
                if (string.IsNullOrEmpty(text)) continue;
                Match m = reMember.Match(text);
                if (!m.Success) continue;
                int start = Sci.PositionFromLine(line) + m.Index;
                Sci.SetSel(start, start + m.Length);
                Sci.ReplaceSel(modifier + m.Value);
                return;
            }
        }

        private static void RemoveModifier(ScintillaNet.ScintillaControl Sci, MemberModel member, string modifier)
        {
            for(int line = member.LineFrom; line <= member.LineTo; line++)
            {
                string text = Sci.GetLine(line);
                if (string.IsNullOrEmpty(text)) continue;
                Match m = Regex.Match(text, modifier);
                if (!m.Success) continue;
                int start = Sci.PositionFromLine(line) + m.Index;
                Sci.SetSel(start, start + m.Length);
                Sci.ReplaceSel("");
                return;
            }
        }

        private static void FixModifiersLocation(ScintillaNet.ScintillaControl Sci, MemberModel member)
        {
            for (int line = member.LineFrom; line <= member.LineTo; line++)
            {
                string text = Sci.GetLine(line);
                if (string.IsNullOrEmpty(text) || !reMember.IsMatch(text)) continue;
                Match m1 = reModifiers.Match(text);
                if (!m1.Success) continue;
                Group decl = m1.Groups[2];
                Match m2 = reModifier.Match(decl.Value);
                if (!m2.Success) continue;
                int start = Sci.PositionFromLine(line);
                Sci.SetSel(start + decl.Index, start + decl.Length);
                Sci.ReplaceSel((m2.Value + decl.Value.Remove(m2.Index, m2.Length)).TrimEnd());
                return;
            }
        }

        private static void FixFinalModifierLocation(ScintillaNet.ScintillaControl Sci, MemberModel member)
        {
            for (int line = member.LineFrom; line <= member.LineTo; line++)
            {
                string text = Sci.GetLine(line);
                if (string.IsNullOrEmpty(text) || !reMember.IsMatch(text)) continue;
                Match m = Regex.Match(text.Trim(), "@:final\\s");
                if (!m.Success) continue;
                if (m.Index == 0) return;
                Group decl = m.Groups[0];
                m = Regex.Match(text, "[@:a-z ]", RegexOptions.IgnoreCase);
                int insertStart = m.Success ? m.Index : 0;
                int start = Sci.PositionFromLine(line);
                Sci.SetSel(start, start + text.Length);
                Sci.ReplaceSel(text.Remove(decl.Index, decl.Length).Insert(insertStart, decl.Value));
                return;
            }
        }

        private static void FixInlineModifierLocation(ScintillaNet.ScintillaControl Sci, MemberModel member)
        {
            for (int line = member.LineFrom; line <= member.LineTo; line++)
            {
                string text = Sci.GetLine(line);
                if (string.IsNullOrEmpty(text)) continue;
                Match m = Regex.Match(text.Trim(), "inline\\s");
                if (!m.Success) continue;
                int start = Sci.PositionFromLine(line);
                Sci.SetSel(start, start + text.Length);
                text = text.Remove(m.Index, m.Length);
                m = reMember.Match(text);
                Sci.ReplaceSel(text.Insert(m.Index, "inline "));
                return;
            }
        }

        private static void FixNoCompletionMetaLocation(ScintillaNet.ScintillaControl Sci, MemberModel member)
        {
            for (int line = member.LineFrom; line <= member.LineTo; line++)
            {
                string text = Sci.GetLine(line);
                if (string.IsNullOrEmpty(text)) continue;
                Match m = Regex.Match(text.Trim(), "@:noCompletion\\s");
                if (!m.Success) continue;
                if (m.Index == 0) return;
                Group decl = m.Groups[0];
                m = Regex.Match(text, "[@:a-z ]", RegexOptions.IgnoreCase);
                int insertStart = m.Success ? m.Groups[0].Index : 0;
                int start = Sci.PositionFromLine(line);
                Sci.SetSel(start, start + text.Length);
                Sci.ReplaceSel(text.Remove(decl.Index, decl.Length).Insert(insertStart, decl.Value));
                return;
            }
        }
    }

    class FoundDeclaration
    {
        public MemberModel member;
        public ClassModel inClass = ClassModel.VoidClass;

        public FoundDeclaration()
        {
        }

        public bool GetIsEmpty()
        {
            return member == null && inClass == ClassModel.VoidClass;
        }
    }

    /// <summary>
    /// Available generators
    /// </summary>
    public enum GeneratorJobType : int
    {
        MakeClassFinal,
        MakeClassNotFinal,
        MakeClassExtern,
        MakeClassNotExtern,
        MakeMethodFinal,
        MakeMethodNotFinal,
        AddStaticModifier,
        RemoveStaticModifier,
        AddInlineModifier,
        RemoveInlineModifier,
        AddNoCompletionMeta,
        RemoveNoCompletionMeta,
        ChangeAccess,
    }

    /// <summary>
    /// Generation completion list item
    /// </summary>
    class GeneratorItem : ICompletionListItem
    {
        private string label;
        private GeneratorJobType job;
        private MemberModel member;
        private ClassModel inClass;
        private object data;

        public GeneratorItem(string label, GeneratorJobType job, MemberModel member, ClassModel inClass)
        {
            this.label = label;
            this.job = job;
            this.member = member;
            this.inClass = inClass;
        }

        public GeneratorItem(string label, GeneratorJobType job, MemberModel member, ClassModel inClass, object data)
            : this(label, job, member, inClass)
        {

            this.data = data;
        }

        public string Label
        {
            get { return label; }
        }

        public string Description
        {
            get { return TextHelper.GetString("Info.GeneratorTemplate"); }
        }

        public System.Drawing.Bitmap Icon
        {
            get { return (System.Drawing.Bitmap)ASContext.Panel.GetIcon(PluginUI.ICON_DECLARATION); }
        }

        public string Value
        {
            get
            {
                PluginMain.GenerateJob(job, member, inClass, label, data);
                return null;
            }
        }

        public object Data
        {
            get { return data; }
        }
    }
}