using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

/*
 * THIS IS NOT FINISHED.
 * IS MEANT TO BE A LITTLE WINDOW THAT LETS YOU SWITCH BETWEEN
 * GIT / LOCAL SOURCE FOR PACKAGES, SO THAT THEY CAN EASILY BE
 * EDITED IN THE CONTEXT OF THE PROJECT, THEN COMMITTED AND
 * SWITCHED BACK TO GIT SOURCE
 */
namespace Utils.Editor
{
    public class PackageSwitcher : EditorWindow
    {
        static readonly Lazy<GUIStyle> GitSourceTagStyle = new(() =>
        {
            var style = new GUIStyle("box")
            {
                fontSize = 11,

                // border = new RectOffset(1, 1, 1, 1),
                margin = new RectOffset(-1, -1, 0, 0),
                padding = new RectOffset(5, 4, 3, 2)
            };
            return style;
        });

        static readonly Lazy<GUIContent> LocalPathBrowseIcon = new(() =>
        {
            var content = EditorGUIUtility.IconContent(
                EditorGUIUtility.isProSkin
                    ? "d_FolderOpened Icon"
                    : "FolderOpened Icon");
            content.text = "Local Path";
            return content;
        });

        ListRequest _listRequest;
        AddRequest _addRequest;
        List<LoadedPackage> _packages;
        Vector2 _scrollPos;

        #region MONOBEHAVIOUR METHODS

        void OnEnable()
        {
            _listRequest = Client.List(true, false);
            EditorApplication.update += CheckListRequest;
        }

        void OnGUI()
        {
            if (_listRequest is { Status: StatusCode.InProgress })
            {
                GUILayout.Label("Listing packages...");
            }
            else if (_addRequest is { Status: StatusCode.InProgress })
            {
                GUILayout.Label("Switching package source...");
            }
            else if (_packages != null)
            {
                var width = position.width;
                using var scroll =
                    new GUILayout.ScrollViewScope(_scrollPos, false, true, GUILayout.Width(width));
                foreach (var package in _packages)
                {
                    // Debug.Log($"window width: {width}");
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox,
                        GUILayout.Width(width - 20)))
                    {
                        using (new GUILayout.HorizontalScope())
                        {
                            DrawSourceTag(package.Info.source.ToString());

                            GUILayout.Label(package.Info.displayName, EditorStyles.boldLabel);
                            GUILayout.FlexibleSpace();
                            GUILayout.Label(package.Info.author.name);
                        }

                        GUILayout.Space(8);

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button(LocalPathBrowseIcon.Value,
                                GUILayout.Height(19), GUILayout.Width(120)))
                            {
                                BrowseForLocalPackage(package);
                            }

                            GUILayout.Label($"{package.LocalSourcePath}");
                        }

                        GUILayout.Label($"{package.GitSourcePath}");

                        GUILayout.Space(8);

                        using (new GUILayout.HorizontalScope())
                        {
                            //  __    ___                  __   __    _
                            // / __ |  |     ___ \    |   /  \ /  `  /_\  |
                            // \__| |  |         /    |__ \__/ \__, /   \ |__
                            //

                            using (new EditorGUI.DisabledScope(package.Info.source != PackageSource.Git ||
                                                               package.HasLocalSourcePath == false))
                            {
                                if (GUILayout.Button("Switch to Local"))
                                {
                                    SwitchPackageSource(package, PackageSource.Local);
                                }
                            }

                            using (new EditorGUI.DisabledScope(package.Info.source != PackageSource.Local ||
                                                               package.HasGitSourcePath == false))
                            {
                                if (GUILayout.Button("Switch to Git"))
                                {
                                    SwitchPackageSource(package, PackageSource.Git);
                                }
                            }
                        }
                    }
                }

                _scrollPos = scroll.scrollPosition;
            }
        }

        void DrawSourceTag(string text)
        {
            var color = Color.white;
            GUILayout.Label(text.ToLower(), GitSourceTagStyle.Value);
            var rect = GUILayoutUtility.GetLastRect();
            EditorGUI.DrawRect(new Rect(rect.min.x, rect.min.y, 1f, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.max.x, rect.min.y, 1f, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.min.x, rect.max.y, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.min.x, rect.min.y, rect.width, 1f), color);
        }

        #endregion

        [MenuItem("Window/Package Switcher...", false, 10)]
        static void Init()
        {
            var window = (PackageSwitcher)GetWindow(typeof(PackageSwitcher));
            window.Show();

            window.minSize = new Vector2(700, 70);
            window.maxSize = new Vector2(2000, 1000);
            window.titleContent = new GUIContent("Package Switcher");
        }

        void BrowseForLocalPackage(LoadedPackage package)
        {
            var path = package.HasLocalSourcePath ? package.LocalSourcePath : Application.dataPath;

            path = EditorUtility.OpenFilePanelWithFilters("Select package.json", path,
                new[] { "FileExtension", "json" });

            if (string.IsNullOrEmpty(path) == false)
            {
                if (IsMatchingPackage(package, path))
                {
                    package.LocalSourcePath = Path.GetDirectoryName(path);
                    Debug.Log($"Set local source path to {path}");
                }
            }
        }

        void SwitchPackageSource(LoadedPackage package, PackageSource targetSource)
        {
            if (targetSource == package.Info.source)
            {
                return;
            }

            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (targetSource)
            {
                case PackageSource.Local:
                {
                    if (package.HasLocalSourcePath == false)
                    {
                        throw new Exception($"Package {package.Info.name} does not have a Local Source");
                    }

                    var sourcePath = package.LocalSourcePath;
                    _addRequest = Client.Add("file:" + sourcePath);
                    EditorApplication.update += CheckAddRequest;
                    break;
                }
                case PackageSource.Git:
                {
                    if (package.HasGitSourcePath == false)
                    {
                        throw new Exception(
                            $"Package {package.Info.name} does not have a Git repository source set");
                    }

                    _addRequest = Client.Add(package.GitSourcePath);
                    EditorApplication.update += CheckAddRequest;
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(targetSource), targetSource, null);
            }
        }

        bool IsMatchingPackage(LoadedPackage package, string jsonPath)
        {
            try
            {
                var loadedJson = JsonUtility.FromJson<PackageInfoJson>(File.ReadAllText(jsonPath));
                return loadedJson.name == package.Info.name;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }

        void CheckAddRequest()
        {
            if (_addRequest.IsCompleted)
            {
                switch (_addRequest.Status)
                {
                    case StatusCode.Success:
                        Debug.Log("Installed: " + _addRequest.Result.packageId);
                        break;
                    case >= StatusCode.Failure:
                        Debug.LogError(_addRequest.Error.message);
                        break;
                }

                EditorApplication.update -= CheckAddRequest;
                _addRequest = null;
                Repaint();
            }
        }

        void CheckListRequest()
        {
            if (_listRequest.IsCompleted)
            {
                if (_listRequest.Status == StatusCode.Success)
                {
                    _packages = new List<LoadedPackage>();
                    foreach (var packageInfo in _listRequest.Result)
                    {
                        if (string.IsNullOrEmpty(packageInfo.author.name) == false)
                        {
                            _packages.Add(new LoadedPackage(packageInfo));
                        }
                    }
                }
                else
                {
                    Debug.LogError(
                        $"{nameof(PackageSwitcher)} failed to fetch list of packages: " +
                        _listRequest.Error.message);
                }

                EditorApplication.update -= CheckListRequest;
                _listRequest = null;
                Repaint();
            }
        }

        [Serializable]
        class PackageInfoJson
        {
            #region PUBLIC AND SERIALIZED FIELDS

            public string name;

            #endregion
        }

        class LoadedPackage
        {
            string _localSourcePath;
            string _gitSourcePath;

            public LoadedPackage(PackageInfo info)
            {
                Info = info;
                _localSourcePath = EditorPrefs.GetString(PrefsKey(PackageSource.Local));

                if (Info.repository != null && info.repository.type == "git")
                {
                    GitSourcePath = Info.repository.url;
                }
            }

            #region PROPERTIES

            public PackageInfo Info { get; }

            public string LocalSourcePath
            {
                get => _localSourcePath;
                set
                {
                    _localSourcePath = value;
                    EditorPrefs.SetString(PrefsKey(PackageSource.Local), value);
                }
            }

            public bool HasLocalSourcePath => string.IsNullOrEmpty(LocalSourcePath) == false;

            public string GitSourcePath
            {
                get => _gitSourcePath;
                set
                {
                    _gitSourcePath = value;
                    EditorPrefs.SetString(PrefsKey(PackageSource.Git), value);
                }
            }

            public bool HasGitSourcePath => string.IsNullOrEmpty(GitSourcePath) == false;

            #endregion

            string PrefsKey(PackageSource source)
            {
                // ReSharper disable once SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault
                var locStr = source switch
                {
                    PackageSource.Git   => "git",
                    PackageSource.Local => "local",
                    _                   => throw new ArgumentOutOfRangeException(nameof(source), source, null)
                };
                return $"{nameof(PackageSwitcher)}.{Info.name}.{locStr}";
            }
        }
    }
}