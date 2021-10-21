using System;
using AI.BehaviorTree;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.UIElements;


public class BehaviorTreeEditor : EditorWindow
{
    private BehaviorTreeView _treeView;
    private InspectorView _inspectorView;
    private IMGUIContainer _blackboardView;

    private SerializedObject treeObject;
    private SerializedProperty blackboardProperty;
    
    [MenuItem("AI/Behavior Tree/Open Editor")]
    public static void OpenWindow()
    {
        BehaviorTreeEditor wnd = GetWindow<BehaviorTreeEditor>();
        wnd.titleContent = new GUIContent("BehaviorTreeEditor");
    }

    [OnOpenAsset]
    public static bool OnOpenAsset(int instanceId, int line)
    {
        if (Selection.activeObject is BehaviorTree)
        {
            OpenWindow();
            return true;
        }

        return false;
    }

    private void OnEnable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    

    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
    }
    
    private void OnPlayModeStateChanged(PlayModeStateChange obj)
    {
        switch (obj)
        {
            case PlayModeStateChange.EnteredEditMode:
                OnSelectionChange();
                break;
            case PlayModeStateChange.ExitingEditMode:
                break;
            case PlayModeStateChange.EnteredPlayMode:
                OnSelectionChange();
                break;
            case PlayModeStateChange.ExitingPlayMode:
                break;
        }
    }

    public void CreateGUI()
    {
        // Each editor window contains a root VisualElement object
        VisualElement root = rootVisualElement;

        // Import UXML
        var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_AI_BehaviorTree/Editor/BehaviorTreeEditor.uxml");
        visualTree.CloneTree(root);

        // A stylesheet can be added to a VisualElement.
        // The style will be applied to the VisualElement and all of its children.
        var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/_AI_BehaviorTree/Editor/BehaviorTreeEditor.uss");
        root.styleSheets.Add(styleSheet);

        _treeView = root.Q <BehaviorTreeView>();
        _inspectorView = root.Q <InspectorView>();
        _blackboardView = root.Q<IMGUIContainer>();
        _blackboardView.onGUIHandler = () =>
        {
            treeObject.Update();
            EditorGUILayout.PropertyField(blackboardProperty);
            treeObject.ApplyModifiedProperties();
        };
        _treeView.OnNodeSelected = OnNodeSelectionChanged;
        
        OnSelectionChange();
    }

    private void OnSelectionChange()
    {
        BehaviorTree tree = Selection.activeObject as BehaviorTree;

        if (!tree)
        {
            if (!Selection.activeGameObject) return;
            
            BehaviorTreeOwner owner = Selection.activeGameObject.GetComponent<BehaviorTreeOwner>();
            
            if (!owner) return;

            tree = owner.tree;
        }

        if (tree)
        {
            _treeView.PopulateView(tree);
            treeObject = new SerializedObject(tree);
            blackboardProperty = treeObject.FindProperty("blackboard");
        }
    }

    void OnNodeSelectionChanged(NodeView nodeView)
    {
        _inspectorView.UpdateSelection(nodeView);
    }

    private void OnInspectorUpdate()
    {
        _treeView.UpdateRuntimeNodeState();
    }
}