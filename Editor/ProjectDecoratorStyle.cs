// Copyright (c) 2026 VRCLearn.
// Settings style definitions adapted from WooshiiDev/HierarchyDecorator.
// Original portions: Copyright (c) 2020 - 2024 Damian Slocombe. MIT License.
using UnityEditor;
using UnityEngine;

namespace VRCLearn.ProjectDecorator.Editor
{
    internal static class ProjectDecoratorStyle
    {
        internal static readonly GUIStyle InspectorPadding = new GUIStyle(EditorStyles.inspectorFullWidthMargins)
        {
            padding = new RectOffset(4, 4, 4, 4)
        };
        internal static readonly GUIStyle Title = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 18,
            fixedHeight = 21
        };
        internal static readonly GUIStyle TabBackground = new GUIStyle(EditorStyles.helpBox)
        {
            stretchHeight = true,
            fixedHeight = 0,
            margin = new RectOffset(0, 0, 0, 0),
            padding = new RectOffset(8, 8, 8, 8),
            alignment = TextAnchor.MiddleLeft
        };
        internal static readonly GUIStyle BoxHeader = new GUIStyle(GUI.skin.box)
        {
            stretchHeight = true,
            stretchWidth = true,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            fontSize = 12,
            normal = { textColor = EditorStyles.label.normal.textColor },
            margin = new RectOffset(0, 0, 0, 0),
            padding = new RectOffset(0, 0, 0, 0)
        };
        internal static readonly GUIStyle TabButton = new GUIStyle(EditorStyles.miniButton)
        {
            fixedHeight = 32f,
            fontSize = 12
        };
    }
}
