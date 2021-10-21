using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

public class InspectorView : VisualElement
{
    public new class UxmlFactory : UxmlFactory<InspectorView, UxmlTraits> { }

    private Editor _editor;
    public InspectorView() {
        
    }

    public void UpdateSelection(NodeView nodeView)
    {
       Clear();
       
       UnityEngine.Object.DestroyImmediate(_editor);

       _editor = Editor.CreateEditor(nodeView.node);
       IMGUIContainer container = new IMGUIContainer(() =>
       {
           if (_editor.target)
            _editor.OnInspectorGUI();
       });
       Add(container);
    }
}
