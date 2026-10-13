using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fieldmate.Knowledge;
using Fieldmate.Procedures;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Fieldmate.Editor
{
    /// <summary>
    /// Fieldmate → Procedure Editor (design.md §5.3, #11): create a procedure asset (from the demo or blank), duplicate it,
    /// reorder and edit its steps and safety rules with parts picked from the machine, validate it against the machine
    /// prefab and the manual, and enter Play mode at any step. Block-scoped namespace: Unity maps EditorWindows to
    /// MonoScripts by namespace and class, like MonoBehaviours.
    /// </summary>
    public sealed class ProcedureEditorWindow : EditorWindow
    {
        public const string Folder = "Assets/_Project/Procedures";
        private const string ManualPath = "Assets/_Project/Manual/manual.json";

        private ProcedureAsset asset;
        private SerializedObject serialized;
        private ReorderableList stepList;
        private ReorderableList ruleList;
        private string[] partIds = new string[0];
        private IReadOnlyList<string> problems;
        private Vector2 scroll;
        private int playStep;

        [MenuItem("Fieldmate/Procedure Editor")]
        public static ProcedureEditorWindow Open()
        {
            var window = GetWindow<ProcedureEditorWindow>("Procedures");
            window.minSize = new Vector2(520f, 420f);
            return window;
        }

        /// <summary>A new asset filled from the code-defined demo (or blank), saved under <see cref="Folder"/>.</summary>
        public static ProcedureAsset CreateAsset(string name, bool fromDemo)
        {
            Directory.CreateDirectory(Folder);
            var created = CreateInstance<ProcedureAsset>();
            if (fromDemo)
            {
                created.CopyFrom(DemoProcedures.ReliefValveReplacement());
            }

            var path = AssetDatabase.GenerateUniqueAssetPath($"{Folder}/{name}.asset");
            AssetDatabase.CreateAsset(created, path);
            AssetDatabase.SaveAssets();
            return created;
        }

        public static ProcedureAsset Duplicate(ProcedureAsset source)
        {
            var path = AssetDatabase.GetAssetPath(source);
            var copy = AssetDatabase.GenerateUniqueAssetPath(path);
            AssetDatabase.CopyAsset(path, copy);
            var duplicate = AssetDatabase.LoadAssetAtPath<ProcedureAsset>(copy);
            duplicate.id += "_copy";
            EditorUtility.SetDirty(duplicate);
            AssetDatabase.SaveAssets();
            return duplicate;
        }

        /// <summary>Validates against the machine prefab and the manual (also used by tests and CI).</summary>
        public static IReadOnlyList<string> Validate(ProcedureAsset procedure)
        {
            var machine = AssetDatabase.LoadAssetAtPath<GameObject>(AssistantBenchBuilder.SkidPrefabPath);
            var manual = MachineManual.Parse(File.ReadAllText(ManualPath));
            return ProcedureValidator.Check(procedure, machine, manual);
        }

        public void Select(ProcedureAsset procedure)
        {
            asset = procedure;
            problems = null;
            serialized = asset != null ? new SerializedObject(asset) : null;
            stepList = serialized != null ? BuildStepList() : null;
            ruleList = serialized != null ? BuildRuleList() : null;
        }

        private void OnEnable()
        {
            var machine = AssetDatabase.LoadAssetAtPath<GameObject>(AssistantBenchBuilder.SkidPrefabPath);
            partIds = machine != null ? ProcedureValidator.PartIds(machine) : new string[0];
            if (asset != null)
            {
                Select(asset);
            }
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var picked = (ProcedureAsset)EditorGUILayout.ObjectField(asset, typeof(ProcedureAsset), false, GUILayout.Width(220f));
                if (picked != asset)
                {
                    Select(picked);
                }

                if (GUILayout.Button("New from demo", EditorStyles.toolbarButton)) Select(CreateAsset("ReliefValveReplacement", fromDemo: true));
                if (GUILayout.Button("New blank", EditorStyles.toolbarButton)) Select(CreateAsset("Procedure", fromDemo: false));
                using (new EditorGUI.DisabledScope(asset == null))
                {
                    if (GUILayout.Button("Duplicate", EditorStyles.toolbarButton)) Select(Duplicate(asset));
                }

                GUILayout.FlexibleSpace();
            }

            if (asset == null)
            {
                EditorGUILayout.HelpBox("Pick a procedure asset, or create one from the demo. The bench scene runs the code-defined demo " +
                                        "unless Machine Services is given an asset.", MessageType.Info);
                return;
            }

            serialized.Update();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.PropertyField(serialized.FindProperty("id"));
            EditorGUILayout.PropertyField(serialized.FindProperty("title"));
            EditorGUILayout.PropertyField(serialized.FindProperty("timeLimitSeconds"));
            EditorGUILayout.Space();
            stepList.DoLayoutList();
            ruleList.DoLayoutList();
            EditorGUILayout.EndScrollView();
            serialized.ApplyModifiedProperties();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Validate", GUILayout.Width(100f)))
                {
                    problems = Validate(asset);
                }

                var names = asset.steps.Select((s, i) => $"{i + 1}. {s.title}").ToArray();
                playStep = Mathf.Clamp(playStep, 0, Mathf.Max(0, names.Length - 1));
                playStep = EditorGUILayout.Popup(playStep, names);
                using (new EditorGUI.DisabledScope(names.Length == 0 || EditorApplication.isPlaying))
                {
                    if (GUILayout.Button("Play from step", GUILayout.Width(110f)))
                    {
                        PlayFrom(asset.steps[playStep].id);
                    }
                }
            }

            if (problems != null)
            {
                EditorGUILayout.HelpBox(problems.Count == 0 ? "No problems: every part, socket, tool and state exists." : string.Join("\n", problems),
                    problems.Count == 0 ? MessageType.Info : MessageType.Error);
            }
        }

        /// <summary>Enters Play mode with the procedure advanced to <paramref name="stepId"/> (see PlayFromStep).</summary>
        public static void PlayFrom(string stepId)
        {
            PlayerPrefs.SetString(PlayFromStep.PrefsKey, stepId);
            PlayerPrefs.Save();
            EditorApplication.EnterPlaymode();
        }

        private ReorderableList BuildStepList()
        {
            var list = new ReorderableList(serialized, serialized.FindProperty("steps"), true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Steps (drag to reorder)"),
                elementHeightCallback = i => StepLines(serialized.FindProperty("steps").GetArrayElementAtIndex(i)) * (EditorGUIUtility.singleLineHeight + 2f) + 6f,
            };
            list.drawElementCallback = (rect, i, active, focused) =>
            {
                var step = list.serializedProperty.GetArrayElementAtIndex(i);
                var line = new Rect(rect.x, rect.y + 2f, rect.width, EditorGUIUtility.singleLineHeight);
                var kind = step.FindPropertyRelative("kind");
                Row(ref line, step, "title", $"{i + 1}.");
                Row(ref line, step, "id");
                EditorGUI.PropertyField(line, kind);
                line.y += line.height + 2f;
                var k = (StepKind)kind.enumValueIndex;
                if (k != StepKind.Confirm)
                {
                    PartRow(ref line, step.FindPropertyRelative("partId"), k == StepKind.Tool ? "Socket" : "Part");
                }

                if (k == StepKind.Inspect) Row(ref line, step, "dwellSeconds");
                if (k == StepKind.Operate) Row(ref line, step, "targetState");
                if (k == StepKind.Tool) PartRow(ref line, step.FindPropertyRelative("toolId"), "Tool");
                if (k == StepKind.Measure)
                {
                    Row(ref line, step, "expectedValue");
                    Row(ref line, step, "tolerance");
                    Row(ref line, step, "unit");
                }
            };
            return list;
        }

        private static int StepLines(SerializedProperty step) => (StepKind)step.FindPropertyRelative("kind").enumValueIndex switch
        {
            StepKind.Confirm => 3,
            StepKind.Measure => 7,
            StepKind.Tool => 5,
            _ => 5,
        };

        private ReorderableList BuildRuleList()
        {
            var list = new ReorderableList(serialized, serialized.FindProperty("rules"), true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Safety rules: required part must be in its state before the guarded part is touched"),
                elementHeightCallback = _ => 6 * (EditorGUIUtility.singleLineHeight + 2f) + 6f,
            };
            list.drawElementCallback = (rect, i, active, focused) =>
            {
                var rule = list.serializedProperty.GetArrayElementAtIndex(i);
                var line = new Rect(rect.x, rect.y + 2f, rect.width, EditorGUIUtility.singleLineHeight);
                Row(ref line, rule, "description");
                Row(ref line, rule, "id");
                PartRow(ref line, rule.FindPropertyRelative("guardedPartId"), "Guarded part");
                PartRow(ref line, rule.FindPropertyRelative("requiredPartId"), "Required part");
                Row(ref line, rule, "requiredState");
                Row(ref line, rule, "exceptWhenGuardedIs", "Except when guarded is");
            };
            return list;
        }

        private static void Row(ref Rect line, SerializedProperty parent, string field, string label = null)
        {
            var property = parent.FindPropertyRelative(field);
            if (label != null)
            {
                EditorGUI.PropertyField(line, property, new GUIContent(label));
            }
            else
            {
                EditorGUI.PropertyField(line, property);
            }

            line.y += line.height + 2f;
        }

        // A part picked from the machine (free text stays possible for a part that doesn't exist yet).
        private void PartRow(ref Rect line, SerializedProperty property, string label)
        {
            var index = System.Array.IndexOf(partIds, property.stringValue);
            var popup = new Rect(line.x, line.y, line.width - 160f, line.height);
            var text = new Rect(line.xMax - 155f, line.y, 155f, line.height);
            var picked = EditorGUI.Popup(popup, label, index, partIds);
            if (picked != index && picked >= 0)
            {
                property.stringValue = partIds[picked];
            }

            property.stringValue = EditorGUI.TextField(text, property.stringValue);
            line.y += line.height + 2f;
        }
    }
}
