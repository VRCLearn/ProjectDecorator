// Copyright (c) 2026 VRCLearn.
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VRCLearn.ProjectDecorator.Editor
{
    internal enum SettingsLanguage
    {
        Auto,
        English,
        Japanese,
        Korean,
        TraditionalChinese,
        SimplifiedChinese
    }

    [InitializeOnLoad]
    internal static class SettingsLocalization
    {
        private const string PreferenceKey = "com.vrclearn.projectdecorator.language";
        private static readonly string[] LanguageCodes = { "auto", "en", "ja", "ko", "zh-Hant", "zh-Hans" };
        private static readonly string[] LanguageNames = { "Auto", "English", "日本語", "한국어", "繁體中文", "简体中文" };

        private static readonly Dictionary<string, string[]> Translations = new Dictionary<string, string[]>
        {
            { "Language", new[] { "言語", "언어", "語言", "语言" } },
            { "Restore Default Settings", new[] { "既定の設定に戻す", "기본 설정 복원", "恢復預設設定", "恢复默认设置" } },
            { "Restore", new[] { "元に戻す", "복원", "恢復", "恢复" } },
            { "GitHub Repository", new[] { "GitHub リポジトリ", "GitHub 저장소", "GitHub 儲存庫", "GitHub 仓库" } },
            { "General", new[] { "一般", "일반", "一般", "常规" } },
            { "Visual", new[] { "外観", "모양", "外觀", "外观" } },
            { "Icons", new[] { "アイコン", "아이콘", "圖示", "图标" } },
            { "Folder Color", new[] { "フォルダーの色", "폴더 색상", "資料夾色彩", "文件夹颜色" } },
            { "Star", new[] { "星", "별", "星形", "五角星" } },
            { "Clear Color", new[] { "色をクリア", "색상 지우기", "清除色彩", "清除颜色" } },
            { "Add Color…", new[] { "色を追加…", "색 추가…", "新增色彩…", "添加颜色…" } },
            { "Choose Project Icon…", new[] { "プロジェクトのアイコンを選択…", "프로젝트 아이콘 선택…", "選擇專案圖示…", "选择项目图标…" } },
            { "Remove from Palette", new[] { "パレットから削除", "팔레트에서 제거", "從面板移除", "从面板移除" } },
            { "Remove", new[] { "削除", "제거", "移除", "移除" } },
            { "Cancel", new[] { "キャンセル", "취소", "取消", "取消" } },
            { "Default Icon", new[] { "既定のアイコン", "기본 아이콘", "預設圖示", "默认图标" } },
            { "Background", new[] { "背景", "배경", "背景", "背景" } },
            { "Move Up", new[] { "上へ移動", "위로 이동", "上移", "上移" } },
            { "Move Down", new[] { "下へ移動", "아래로 이동", "下移", "下移" } },
            { "Show", new[] { "表示", "표시", "顯示", "显示" } },
            { "Decorate Folder", new[] { "フォルダーの装飾", "폴더 꾸미기", "裝飾資料夾", "装饰文件夹" } },
            { "Folder Icon", new[] { "フォルダーアイコン", "폴더 아이콘", "資料夾圖示", "文件夹图标" } },
            { "Project Settings", new[] { "Project 設定", "Project 설정", "Project 設定", "Project 设置" } },
            { "Restore the default Project settings?", new[] { "Project の設定を既定値に戻しますか？", "Project 기본 설정을 복원할까요?", "要恢復 Project 的預設設定嗎？", "要恢复 Project 的默认设置吗？" } },
            { "Personal project settings", new[] { "プロジェクトごとの個人設定", "프로젝트별 개인 설정", "專案個人設定", "项目个人设置" } },
            { "Folder Decorations", new[] { "フォルダー装飾", "폴더 꾸미기", "資料夾裝飾", "文件夹装饰" } },
            { "Enable Folder Decorations", new[] { "フォルダー装飾を有効化", "폴더 꾸미기 활성화", "啟用資料夾裝飾", "启用文件夹装饰" } },
            { "Favorite Stars", new[] { "お気に入りの星を表示", "즐겨찾기 별 표시", "顯示收藏星號", "显示收藏星标" } },
            { "Alternating Tree Row Backgrounds", new[] { "ツリー行の交互背景", "트리 행 교차 배경", "樹狀列交替背景", "树状行交替背景" } },
            { "Favorites", new[] { "お気に入り", "즐겨찾기", "收藏", "收藏" } },
            { "Project Favorites", new[] { "Project お気に入り", "Project 즐겨찾기", "Project 收藏", "Project 收藏" } },
            { "Open Favorites", new[] { "お気に入りを開く", "즐겨찾기 열기", "開啟收藏", "打开收藏" } },
            { "Add Selected Assets", new[] { "選択中のアセットを追加", "선택한 에셋 추가", "新增選取資源", "添加所选资源" } },
            { "Add Favorite", new[] { "お気に入りに追加", "즐겨찾기에 추가", "加入收藏", "加入收藏" } },
            { "Remove Favorite", new[] { "お気に入りから削除", "즐겨찾기에서 제거", "取消收藏", "取消收藏" } },
            { "Missing asset", new[] { "アセットが見つかりません", "누락된 에셋", "遺失的資源", "缺失的资源" } },
            { "No matching favorites", new[] { "一致するお気に入りはありません", "일치하는 즐겨찾기가 없습니다", "沒有符合的收藏", "没有匹配的收藏" } },
            { "Drag assets or folders here", new[] { "アセットやフォルダーをここにドラッグ", "에셋이나 폴더를 여기로 드래그", "將資源或資料夾拖曳至此", "将资源或文件夹拖到这里" } },
            { "Hold Alt to color the tree region", new[] { "Alt を押してツリー範囲に色を付ける", "Alt 키로 트리 영역에 색 지정", "按住 Alt 為樹狀區域著色", "按住 Alt 为树状区域着色" } },
            { "Coloring the folder tree region", new[] { "フォルダーツリー範囲に色を付ける", "폴더 트리 영역에 색 지정", "為資料夾樹狀區域著色", "为文件夹树状区域着色" } },
            { "Remove this color and clear all folder and tree region colors that use it in this project?", new[] { "この色を削除し、このプロジェクトで使用されているフォルダーとツリー範囲の色をクリアしますか？", "이 색을 제거하고 프로젝트에서 이 색을 쓰는 모든 폴더와 트리 영역 색을 지울까요?", "要移除此色彩，並清除專案中所有使用它的資料夾及樹狀區域色彩嗎？", "要移除此颜色，并清除项目中所有使用它的文件夹和树状区域颜色吗？" } },
        };

        private static readonly Dictionary<string, GUIContent> Contents = new Dictionary<string, GUIContent>();
        private static readonly Dictionary<string, GUIContent[]> EnumContents = new Dictionary<string, GUIContent[]>();
        private static SettingsLanguage language = SettingsLanguage.Auto;
        private static SettingsLanguage automaticLanguage = SettingsLanguage.English;

        static SettingsLocalization()
        {
            // DockArea can deserialize GUIContent owners before Unity permits native
            // preference and system-language queries. Static lookups stay managed-only.
            EditorApplication.delayCall += InitializeLanguage;
        }

        private static void InitializeLanguage()
        {
            automaticLanguage = AutomaticLanguage();
            language = LoadLanguage();
            // Auto may still be Auto while its resolved language changed from the
            // safe English fallback. Refresh existing GUIContent references as well.
            RefreshContents();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        private static void RefreshContents()
        {
            foreach (KeyValuePair<string, GUIContent> entry in Contents)
            {
                int separator = entry.Key.IndexOf('\0');
                entry.Value.text = Text(entry.Key.Substring(0, separator));
                entry.Value.tooltip = Text(entry.Key.Substring(separator + 1));
            }
            LanguageChanged?.Invoke();
        }

        internal static event Action LanguageChanged;

        private static SettingsLanguage LoadLanguage()
        {
            int index = Array.IndexOf(LanguageCodes, EditorPrefs.GetString(PreferenceKey, "auto"));
            return index < 0 ? SettingsLanguage.English : (SettingsLanguage)index;
        }

        private static SettingsLanguage AutomaticLanguage()
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.Japanese:
                    return SettingsLanguage.Japanese;
                case SystemLanguage.Korean:
                    return SettingsLanguage.Korean;
                case SystemLanguage.ChineseTraditional:
                    return SettingsLanguage.TraditionalChinese;
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified:
                    return SettingsLanguage.SimplifiedChinese;
                default:
                    return SettingsLanguage.English;
            }
        }

        internal static SettingsLanguage Language
        {
            get => language;
            set
            {
                if ((int)value < 0 || (int)value >= LanguageCodes.Length || language == value)
                    return;

                language = value;
                EditorPrefs.SetString(PreferenceKey, LanguageCodes[(int)value]);

                RefreshContents();
            }
        }

        internal static string Text(string source)
        {
            SettingsLanguage resolvedLanguage = language == SettingsLanguage.Auto ? automaticLanguage : language;
            if (string.IsNullOrEmpty(source) || resolvedLanguage == SettingsLanguage.English)
                return source;

            return Translations.TryGetValue(source, out string[] translations) ? translations[(int)resolvedLanguage - (int)SettingsLanguage.Japanese] : source;
        }

        internal static GUIContent Content(string text, string tooltip = null)
        {
            string key = text + "\0" + tooltip;
            if (!Contents.TryGetValue(key, out GUIContent content))
            {
                content = new GUIContent(Text(text), Text(tooltip));
                Contents.Add(key, content);
            }
            return content;
        }

        internal static GUIContent Icon(string icon, string tooltip)
        {
            GUIContent content = Content(string.Empty, tooltip);
            content.image = EditorGUIUtility.IconContent(icon).image;
            return content;
        }

        internal static GUIContent[] EnumLabels(string[] names)
        {
            string key = string.Join("\0", names);
            if (!EnumContents.TryGetValue(key, out GUIContent[] labels))
            {
                labels = new GUIContent[names.Length];
                for (int i = 0; i < names.Length; i++)
                    labels[i] = Content(names[i]);
                EnumContents.Add(key, labels);
            }
            return labels;
        }

        internal static void DrawLanguageSelector()
        {
            int selected = EditorGUILayout.Popup(Content("Language"), (int)Language, LanguageNames);
            Language = (SettingsLanguage)selected;
        }

    }
}
