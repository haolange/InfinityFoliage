using System;
using UnityEditor;
using UnityEngine;

namespace Landscape.FoliagePipeline.Editor
{
  [CustomEditor(typeof (WindComponent))]
  public class WindComponentEditor : UnityEditor.Editor
  {
    private SerializedProperty _preset;
    private SerializedProperty _sourceWindZone;
    private SerializedProperty _windSettings;
    private SerializedProperty _gustDirection;
    private SerializedProperty _windStrength;
    private SerializedProperty _windSpeed;
    private SerializedProperty _turbulence;
    private SerializedProperty _gustNoise;
    private WindComponentEditor.WindPreset _selectedPreset = WindComponentEditor.WindPreset.ClickToLoad;

    private void OnSceneGUI()
    {
      WindComponent target = this.target as WindComponent;
      if ((UnityEngine.Object) target == (UnityEngine.Object) null)
        return;
      Quaternion rotation = Quaternion.Euler(0.0f, target.transform.eulerAngles.y, 0.0f);
      float handleSize = HandleUtility.GetHandleSize(target.transform.position);
      Handles.color = Color.yellow;
      Handles.ArrowHandleCap(GUIUtility.GetControlID(FocusType.Passive), target.transform.position, rotation, 2f * handleSize, UnityEngine.EventType.Repaint);
      Handles.SphereHandleCap(GUIUtility.GetControlID(FocusType.Passive), target.transform.position, rotation, 0.2f * handleSize, UnityEngine.EventType.Repaint);
    }

    public override void OnInspectorGUI()
    {
      this.serializedObject.Update();
      bool enabled = GUI.enabled;
      EditorGUI.BeginChangeCheck();
      this._selectedPreset = (WindComponentEditor.WindPreset) EditorGUILayout.EnumPopup("Load Preset", (Enum) this._selectedPreset, (GUILayoutOption[]) Array.Empty<GUILayoutOption>());
      if (EditorGUI.EndChangeCheck() && (uint) this._selectedPreset > 0U)
        this.ApplyPreset();
      EditorGUI.BeginChangeCheck();
      EditorGUILayout.PropertyField(this._sourceWindZone, (GUILayoutOption[]) Array.Empty<GUILayoutOption>());
      if (EditorGUI.EndChangeCheck())
      {
        this.serializedObject.ApplyModifiedProperties();
        if (this._sourceWindZone.objectReferenceValue != (UnityEngine.Object) null)
          ((WindComponent) this.target).Zone = (WindZone) this._sourceWindZone.objectReferenceValue;
      }
      if (this._sourceWindZone.objectReferenceValue != (UnityEngine.Object) null)
      {
        EditorGUILayout.HelpBox("Wind settings are loaded from Wind Zone component. Remove the Wind Zone component to manually modify the wind.", MessageType.Info);
        GUI.enabled = false;
      }
      EditorGUI.BeginChangeCheck();
      GUILayout.Label("Wind", EditorStyles.boldLabel, (GUILayoutOption[]) Array.Empty<GUILayoutOption>());
      EditorGUILayout.PropertyField(this._windStrength, (GUILayoutOption[]) Array.Empty<GUILayoutOption>());
      EditorGUILayout.PropertyField(this._windSpeed, (GUILayoutOption[]) Array.Empty<GUILayoutOption>());
      EditorGUILayout.PropertyField(this._turbulence, (GUILayoutOption[]) Array.Empty<GUILayoutOption>());
      GUI.enabled = enabled;
      GUILayout.Label("Noise", EditorStyles.boldLabel, (GUILayoutOption[]) Array.Empty<GUILayoutOption>());
      EditorGUILayout.PropertyField(this._gustNoise, (GUILayoutOption[]) Array.Empty<GUILayoutOption>());
      if (EditorGUI.EndChangeCheck())
      {
        this._selectedPreset = WindComponentEditor.WindPreset.ClickToLoad;
        this.serializedObject.ApplyModifiedProperties();
        ((WindComponent) this.target).Settings.Apply(this._gustNoise.objectReferenceValue as Texture2D);
      }
      if (this._selectedPreset == (WindComponentEditor.WindPreset) this._preset.intValue)
        return;
      this._preset.intValue = (int) this._selectedPreset;
      this.serializedObject.ApplyModifiedProperties();
    }

    private void ApplyPreset()
    {
      if (this._sourceWindZone.objectReferenceValue != (UnityEngine.Object) null)
      {
        if (!EditorUtility.DisplayDialog("Apply Preset?", "The wind settings are driven by a wind zone. Do you want to apply the preset to the source Wind Zone?", "Apply", "Cancel"))
        {
          this._selectedPreset = (WindComponentEditor.WindPreset) this._preset.intValue;
          return;
        }
        Undo.RecordObjects(new UnityEngine.Object[2]
        {
          this.target,
          this._sourceWindZone.objectReferenceValue
        }, "Load Wind Preset");
      }
      else
        Undo.RecordObject(this.target, "Load Wind Preset");
      WindComponent target = (WindComponent) this.target;
      switch (this._selectedPreset)
      {
        case WindComponentEditor.WindPreset.Calm:
          target.Settings = FWindSettings.Calm;
          break;
        case WindComponentEditor.WindPreset.Breeze:
          target.Settings = FWindSettings.Breeze;
          break;
        case WindComponentEditor.WindPreset.StrongBreeze:
          target.Settings = FWindSettings.StrongBreeze;
          break;
        case WindComponentEditor.WindPreset.Storm:
          target.Settings = FWindSettings.Storm;
          break;
      }
      target.Settings = new FWindSettings(target.Settings)
      {
        WindDirection = FWindSettings.RotationToDirection(target.transform.rotation)
      };
      this.serializedObject.Update();
      if (this._sourceWindZone.objectReferenceValue != (UnityEngine.Object) null)
      {
        ((WindComponent) this.target).Settings.ApplyToWindZone((WindZone) this._sourceWindZone.objectReferenceValue);
        EditorUtility.SetDirty(this._sourceWindZone.objectReferenceValue);
      }
      Undo.FlushUndoRecordObjects();
    }

    private void OnEnable()
    {
      this.FindSerializedProperties();
      this.ValidateNoise();
      this._selectedPreset = (WindComponentEditor.WindPreset) this._preset.intValue;
      Undo.undoRedoPerformed += new Undo.UndoRedoCallback(this.OnUndoPerformed);
    }

    private void OnDisable() => Undo.undoRedoPerformed -= new Undo.UndoRedoCallback(this.OnUndoPerformed);

    private void OnUndoPerformed()
    {
      this.serializedObject.Update();
      this._selectedPreset = (WindComponentEditor.WindPreset) this._preset.intValue;
    }

    private void FindSerializedProperties()
    {
      this._preset = this.serializedObject.FindProperty("m_selectedPreset");
      this._sourceWindZone = this.serializedObject.FindProperty("m_sourceWindZone");
      this._windSettings = this.serializedObject.FindProperty("windSettings");
      this._gustDirection = this._windSettings.FindPropertyRelative("GustDirection");
      this._windStrength = this._windSettings.FindPropertyRelative("WindStrength");
      this._windSpeed = this._windSettings.FindPropertyRelative("WindSpeed");
      this._turbulence = this._windSettings.FindPropertyRelative("Turbulence");
      this._gustNoise = this.serializedObject.FindProperty("m_gustNoise");
    }

    private void ValidateNoise()
    {
      if (!(this._gustNoise.objectReferenceValue == (UnityEngine.Object) null))
        return;
      this._gustNoise.objectReferenceValue = (UnityEngine.Object) GlobalWindInitializer.LoadGustNoise();
      this._gustNoise.serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private enum WindPreset
    {
      ClickToLoad,
      Calm,
      Breeze,
      StrongBreeze,
      Storm,
    }
  }
}
