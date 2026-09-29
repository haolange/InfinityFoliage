using UnityEditor;
using UnityEngine;

namespace Landscape.FoliagePipeline.Editor
{
    internal static class TreeOcclusionControls
    {
        internal static void DrawLayout(SerializedProperty property, bool cpu)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, Height);
            Draw(rect, property, cpu);
        }

        internal static float Height
        {
            get { return EditorGUIUtility.singleLineHeight * 2f + EditorGUIUtility.standardVerticalSpacing; }
        }

        internal static void Draw(Rect position, SerializedProperty property, bool cpu)
        {
            position.height = EditorGUIUtility.singleLineHeight;
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            int value = property.intValue;
            EditorGUI.BeginChangeCheck();
            bool terrain = EditorGUI.Toggle(position, "Terrain Occlusion", (value & 1) != 0);
            bool terrainChanged = EditorGUI.EndChangeCheck();
            position.y += position.height + EditorGUIUtility.standardVerticalSpacing;
            bool hzb = (value & 2) != 0;
            bool hzbChanged;
            using (new EditorGUI.DisabledScope(cpu))
            {
                EditorGUI.BeginChangeCheck();
                hzb = EditorGUI.Toggle(position, new GUIContent("GPU HZB", "Additional depth occlusion on the GPU backend."), hzb);
                hzbChanged = EditorGUI.EndChangeCheck();
            }
            if (terrainChanged) { value = terrain ? value | 1 : value & ~1; }
            if (hzbChanged) { value = hzb ? value | 2 : value & ~2; }
            if (terrainChanged || hzbChanged) { property.intValue = value; }
            EditorGUI.showMixedValue = false;
        }
    }

    [CustomPropertyDrawer(typeof(FoliageRenderSettings))]
    public class FoliageRenderSettingsDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            Rect line = position;
            line.height = EditorGUIUtility.singleLineHeight;
            property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, label, true);
            if (property.isExpanded)
            {
                ++EditorGUI.indentLevel;
                line.y += line.height + EditorGUIUtility.standardVerticalSpacing;
                SerializedProperty child = property.Copy();
                SerializedProperty end = property.GetEndProperty();
                bool cpu = property.FindPropertyRelative("treeBackend").intValue == (int)TreeVisibilityBackend.CPU;
                child.NextVisible(true);
                while (!SerializedProperty.EqualContents(child, end))
                {
                    bool occlusion = child.name == "treeOcclusion";
                    line.height = occlusion ? TreeOcclusionControls.Height : EditorGUI.GetPropertyHeight(child, true);
                    if (occlusion) { TreeOcclusionControls.Draw(line, child, cpu); }
                    else { EditorGUI.PropertyField(line, child, true); }
                    line.y += line.height + EditorGUIUtility.standardVerticalSpacing;
                    if (!child.NextVisible(false)) { break; }
                }
                --EditorGUI.indentLevel;
            }
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded) { return height; }
            SerializedProperty child = property.Copy();
            SerializedProperty end = property.GetEndProperty();
            child.NextVisible(true);
            while (!SerializedProperty.EqualContents(child, end))
            {
                height += EditorGUIUtility.standardVerticalSpacing +
                    (child.name == "treeOcclusion" ? TreeOcclusionControls.Height : EditorGUI.GetPropertyHeight(child, true));
                if (!child.NextVisible(false)) { break; }
            }
            return height;
        }
    }
}
