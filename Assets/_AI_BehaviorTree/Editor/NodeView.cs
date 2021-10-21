using System;
using System.Collections;
using System.Collections.Generic;
using AI.BehaviorTree;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.Graphs;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public class NodeView : UnityEditor.Experimental.GraphView.Node
{
    public Action<NodeView> OnNodeSelected;
    public BaseNode node;

    public Port inputPort;
    public Port outputPort;
    public NodeView(BaseNode node) : base("Assets/_AI_BehaviorTree/Editor/NodeView.uxml")
    {
        this.node = node;
        this.title = node.name;
        this.viewDataKey = node.guid;
        style.left = node.graphPosition.x;
        style.top = node.graphPosition.y;

        CreateInputPorts();
        CreateOutputPorts();
        SetupNodeClasses();

        Label descriptionLabel = this.Q<Label>("description");
        descriptionLabel.bindingPath = "description";
        descriptionLabel.Bind(new SerializedObject(node));
    }

    private void SetupNodeClasses()
    {
        if (node is SelectorNode)
        {
            AddToClassList("selector");
        }
        if (node is ActionNode)
        {
            AddToClassList("action");
        }
        else if (node is CompositeNode)
        {
            AddToClassList("composite");  
        }
        else if (node is DecoratorNode)
        {
            AddToClassList("decorator");
        }
        else if (node is RootNode)
        {
            AddToClassList("root");
        }
    }

    private void CreateOutputPorts()
    {
        if (node is ActionNode)
        {
        }
        else if (node is CompositeNode || node is SelectorNode)
        {
            outputPort = InstantiatePort(Orientation.Vertical, Direction.Output, Port.Capacity.Multi, typeof(bool));
        }
        else if (node is DecoratorNode || node is RootNode)
        {
            outputPort = InstantiatePort(Orientation.Vertical, Direction.Output, Port.Capacity.Single, typeof(bool));
        }

        if (outputPort != null)
        {
            outputPort.portName = "";
            outputPort.style.flexDirection = FlexDirection.ColumnReverse;
            outputContainer.Add(outputPort);
        }
    }

    private void CreateInputPorts()
    {
        if (node is ActionNode)
        {
            inputPort = InstantiatePort(Orientation.Vertical, Direction.Input, Port.Capacity.Single, typeof(bool));
        }
        else if (node is CompositeNode)
        {
            inputPort = InstantiatePort(Orientation.Vertical, Direction.Input, Port.Capacity.Single, typeof(bool));
        }
        else if (node is DecoratorNode)
        {
            inputPort = InstantiatePort(Orientation.Vertical, Direction.Input, Port.Capacity.Single, typeof(bool));
        }
        else if (node is RootNode)
        {
            //apply padding
        }
        else if (node is SelectorNode)
        {
            inputPort = InstantiatePort(Orientation.Vertical, Direction.Input, Port.Capacity.Single, typeof(bool));
        }

        if (inputPort != null)
        {
            inputPort.portName = "";
            inputPort.style.flexDirection = FlexDirection.Column;
            inputContainer.Add(inputPort);
        }
    }

    public override void SetPosition(Rect newPos)
    {
        base.SetPosition(newPos);
        Undo.RecordObject(node, "Behavior Tree (Set Position)");
        node.graphPosition.x = newPos.xMin;
        node.graphPosition.y = newPos.yMin;
        EditorUtility.SetDirty(node);
    }

    public override void OnSelected()
    {
        base.OnSelected();
        if (OnNodeSelected != null)
        {
            OnNodeSelected.Invoke(this);
        }
    }

    public void SortChildren()
    {
        CompositeNode compositeNode = node as CompositeNode;

        if (compositeNode)
        {
            compositeNode.children.Sort(SortByHorizontal);
        }
        
        SelectorNode selectorNode = node as SelectorNode;

        if (selectorNode)
        {
            selectorNode.children.Sort(SortByHorizontal);
        }
    }

    private int SortByHorizontal(BaseNode x, BaseNode y)
    {
        return x.graphPosition.x < y.graphPosition.x ? -1 : 1;
    }

    public void RuntimeUpdateState()
    {
        RemoveFromClassList("running");
        RemoveFromClassList("failure");
        RemoveFromClassList("success");
        
        if (!Application.isPlaying) return;

        switch (node.state)
        {
            case BaseNode.State.RUNNING:
                if (node.hasStarted)
                    AddToClassList("running");
                break;
            case BaseNode.State.FAILURE:
                AddToClassList("failure");
                break;
            case BaseNode.State.SUCCESS:
                AddToClassList("success");
                break;
        }
    }
}
