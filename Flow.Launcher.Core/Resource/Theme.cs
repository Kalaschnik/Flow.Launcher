using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Flow.Launcher.Infrastructure;
using Flow.Launcher.Infrastructure.Logger;
using Flow.Launcher.Infrastructure.UserSettings;

namespace Flow.Launcher.Core.Resource
{
    public class Theme
    {
        private readonly List<string> _themeDirectories = new List<string>();
        private ResourceDictionary _oldResource;
        private string _oldTheme;
        public Settings Settings { get; set; }
        private const string Folder = "Themes";
        private const string Extension = ".xaml";
        private string DirectoryPath => Path.Combine(Constant.ProgramDirectory, Folder);
        private string UserDirectoryPath => Path.Combine(DataLocation.DataDirectory(), Folder);

        public Theme()
        {
            _themeDirectories.Add(DirectoryPath);
            _themeDirectories.Add(UserDirectoryPath);
            MakesureThemeDirectoriesExist();

            var dicts = Application.Current.Resources.MergedDictionaries;
            _oldResource = dicts.First(d =>
            {
                if (d.Source == null)
                    return false;

                var p = d.Source.AbsolutePath;
                var dir = Path.GetDirectoryName(p).NonNull();
                var info = new DirectoryInfo(dir);
                var f = info.Name;
                var e = Path.GetExtension(p);
                var found = f == Folder && e == Extension;
                return found;
            });
            _oldTheme = Path.GetFileNameWithoutExtension(_oldResource.Source.AbsolutePath);
        }

        private void MakesureThemeDirectoriesExist()
        {
            foreach (string dir in _themeDirectories)
            {
                if (!Directory.Exists(dir))
                {
                    try
                    {
                        Directory.CreateDirectory(dir);
                    }
                    catch (Exception e)
                    {
                        Log.Exception($"|Theme.MakesureThemeDirectoriesExist|Exception when create directory <{dir}>", e);
                    }
                }
            }
        }

        public bool ChangeTheme(string theme)
        {
            const string defaultTheme = Constant.DefaultTheme;

            string path = GetThemePath(theme);
            try
            {
                if (string.IsNullOrEmpty(path))
                    throw new DirectoryNotFoundException("Theme path can't be found <{path}>");

                Settings.Theme = theme;

                //always allow re-loading default theme, in case of failure of switching to a new theme from default theme
                if (_oldTheme != theme || theme == defaultTheme)
                {
                    UpdateResourceDictionary(GetResourceDictionary());
                    _oldTheme = Path.GetFileNameWithoutExtension(_oldResource.Source.AbsolutePath);
                }

                if (Settings.UseDropShadowEffect)
                    AddDropShadowEffectToCurrentTheme();
            }
            catch (DirectoryNotFoundException e)
            {
                Log.Error($"|Theme.ChangeTheme|Theme <{theme}> path can't be found");
                if (theme != defaultTheme)
                {
                    MessageBox.Show(string.Format(InternationalizationManager.Instance.GetTranslation("theme_load_failure_path_not_exists"), theme));
                    ChangeTheme(defaultTheme);
                }
                return false;
            }
            catch (XamlParseException e)
            {
                Log.Error($"|Theme.ChangeTheme|Theme <{theme}> fail to parse");
                if (theme != defaultTheme)
                {
                    MessageBox.Show(string.Format(InternationalizationManager.Instance.GetTranslation("theme_load_failure_parse_error"), theme));
                    ChangeTheme(defaultTheme);
                }
                return false;
            }
            return true;
        }

        private void UpdateResourceDictionary(ResourceDictionary dictionaryToUpdate)
        {
            var dicts = Application.Current.Resources.MergedDictionaries;

            dicts.Remove(_oldResource);
            dicts.Add(dictionaryToUpdate);
            _oldResource = dictionaryToUpdate;
        }

        private ResourceDictionary CurrentThemeResourceDictionary()
        {
            var uri = GetThemePath(Settings.Theme);
            var dict = new ResourceDictionary
            {
                Source = new Uri(uri, UriKind.Absolute)
            };

            return dict;
        }

        public ResourceDictionary GetResourceDictionary()
        {
            var dict = CurrentThemeResourceDictionary();

            Style queryBoxStyle = dict["QueryBoxStyle"] as Style;
            if (queryBoxStyle != null)
            {
                queryBoxStyle.Setters.Add(new Setter(TextBox.FontFamilyProperty, new FontFamily(Settings.QueryBoxFont)));
                queryBoxStyle.Setters.Add(new Setter(TextBox.FontStyleProperty, FontHelper.GetFontStyleFromInvariantStringOrNormal(Settings.QueryBoxFontStyle)));
                queryBoxStyle.Setters.Add(new Setter(TextBox.FontWeightProperty, FontHelper.GetFontWeightFromInvariantStringOrNormal(Settings.QueryBoxFontWeight)));
                queryBoxStyle.Setters.Add(new Setter(TextBox.FontStretchProperty, FontHelper.GetFontStretchFromInvariantStringOrNormal(Settings.QueryBoxFontStretch)));

                var caretBrushPropertyValue = queryBoxStyle.Setters.OfType<Setter>().Any(x => x.Property.Name == "CaretBrush");
                var foregroundPropertyValue = queryBoxStyle.Setters.OfType<Setter>().Where(x => x.Property.Name == "Foreground")
                    .Select(x => x.Value).FirstOrDefault();
                if (!caretBrushPropertyValue && foregroundPropertyValue != null) //otherwise BaseQueryBoxStyle will handle styling
                    queryBoxStyle.Setters.Add(new Setter(TextBox.CaretBrushProperty, foregroundPropertyValue));
            }

            Style resultItemStyle = dict["ItemTitleStyle"] as Style;
            Style resultSubItemStyle = dict["ItemSubTitleStyle"] as Style;
            Style resultItemSelectedStyle = dict["ItemTitleSelectedStyle"] as Style;
            Style resultSubItemSelectedStyle = dict["ItemSubTitleSelectedStyle"] as Style;
            if (resultItemStyle != null && resultSubItemStyle != null && resultSubItemSelectedStyle != null && resultItemSelectedStyle != null)
            {
                Setter fontFamily = new Setter(TextBlock.FontFamilyProperty, new FontFamily(Settings.ResultFont));
                Setter fontStyle = new Setter(TextBlock.FontStyleProperty, FontHelper.GetFontStyleFromInvariantStringOrNormal(Settings.ResultFontStyle));
                Setter fontWeight = new Setter(TextBlock.FontWeightProperty, FontHelper.GetFontWeightFromInvariantStringOrNormal(Settings.ResultFontWeight));
                Setter fontStretch = new Setter(TextBlock.FontStretchProperty, FontHelper.GetFontStretchFromInvariantStringOrNormal(Settings.ResultFontStretch));

                Setter[] setters = { fontFamily, fontStyle, fontWeight, fontStretch };
                Array.ForEach(new[] { resultItemStyle, resultSubItemStyle, resultItemSelectedStyle, resultSubItemSelectedStyle }, o => Array.ForEach(setters, p => o.Setters.Add(p)));
            }

            return dict;
        }

        public List<string> LoadAvailableThemes()
        {
            List<string> themes = new List<string>();
            foreach (var themeDirectory in _themeDirectories)
            {
                themes.AddRange(
                    Directory.GetFiles(themeDirectory)
                        .Where(filePath => filePath.EndsWith(Extension) && !filePath.EndsWith("Base.xaml"))
                        .ToList());
            }
            return themes.OrderBy(o => o).ToList();
        }

        private string GetThemePath(string themeName)
        {
            foreach (string themeDirectory in _themeDirectories)
            {
                string path = Path.Combine(themeDirectory, themeName + Extension);
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return string.Empty;
        }

        public void AddDropShadowEffectToCurrentTheme()
        {
            var dict = CurrentThemeResourceDictionary();

            var windowBorderStyle = dict["WindowBorderStyle"] as Style;

            var effectSetter = new Setter();
            effectSetter.Property = Border.EffectProperty;
            effectSetter.Value = new DropShadowEffect
            {
                Opacity = 0.9,
                ShadowDepth = 2,
                BlurRadius = 15
            };

            windowBorderStyle.Setters.Add(effectSetter);

            UpdateResourceDictionary(dict);
        }

        public void RemoveDropShadowEffectToCurrentTheme()
        {
            var dict = CurrentThemeResourceDictionary();
            var windowBorderStyle = dict["WindowBorderStyle"] as Style;

            dict.Remove(Border.EffectProperty);

            UpdateResourceDictionary(dict);
        }

        #region Blur Handling
        /*
        Found on https://github.com/riverar/sample-win10-aeroglass
        */
        private enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_GRADIENT = 1,
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
            ACCENT_ENABLE_BLURBEHIND = 3,
            ACCENT_INVALID_STATE = 4
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public AccentState AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        private enum WindowCompositionAttribute
        {
            WCA_ACCENT_POLICY = 19
        }
        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        /// <summary>
        /// Sets the blur for a window via SetWindowCompositionAttribute
        /// </summary>
        public void SetBlurForWindow()
        {

            // Exception of FindResource can't be cathed if global exception handle is set
            if (Environment.OSVersion.Version >= new Version(6, 2))
            {
                var resource = Application.Current.TryFindResource("ThemeBlurEnabled");
                bool blur;
                if (resource is bool)
                {
                    blur = (bool)resource;
                }
                else
                {
                    blur = false;
                }

                if (blur)
                {
                    SetWindowAccent(Application.Current.MainWindow, AccentState.ACCENT_ENABLE_BLURBEHIND);
                }
                else
                {
                    SetWindowAccent(Application.Current.MainWindow, AccentState.ACCENT_DISABLED);
                }
            }
        }

        private void SetWindowAccent(Window w, AccentState state)
        {
            var windowHelper = new WindowInteropHelper(w);
            var accent = new AccentPolicy { AccentState = state };
            var accentStructSize = Marshal.SizeOf(accent);

            var accentPtr = Marshal.AllocHGlobal(accentStructSize);
            Marshal.StructureToPtr(accent, accentPtr, false);

            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                SizeOfData = accentStructSize,
                Data = accentPtr
            };

            SetWindowCompositionAttribute(windowHelper.Handle, ref data);

            Marshal.FreeHGlobal(accentPtr);
        }
        #endregion
    }
}
