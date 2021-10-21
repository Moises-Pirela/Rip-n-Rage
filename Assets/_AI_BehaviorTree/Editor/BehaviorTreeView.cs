using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AI.BehaviorTree;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.Experimental.GraphView;

public class BehaviorTreeView : GraphView
{
    public new class UxmlFactory : UxmlFactory<BehaviorTreeView, UxmlTraits> { }

    public Action<NodeView> OnNodeSelected;

    private BehaviorTree tree;

    public BehaviorTreeView()
    {
        Insert(0, new GridBackground());

        this.AddManipulator(new ContentZoomer());
        this.AddManipulator(new ContentDragger());
        this.AddManipulator(new SelectionDragger());
        this.AddManipulator(new RectangleSelector());
        
        var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/_AI_BehaviorTree/Editor/BehaviorTreeEditor.uss");
        styleSheets.Add(styleSheet);

        Undo.undoRedoPerformed += OnUndoRedo;
    }

    private void OnUndoRedo()
    {
        PopulateView(tree);
        AssetDatabase.SaveAssets();
    }

    NodeView FindNodeView(BaseNode node)
    {
        return GetNodeByGuid(node.guid) as NodeView;
    }

    public void PopulateView(BehaviorTree tree)
    {
        this.tree = tree;

        graphViewChanged -= OnGraphViewChanged;
        DeleteElements(graphElements.ToList());
        graphViewChanged += OnGraphViewChanged;

        if (tree.rootNode == null)
        {
            tree.rootNode = tree.CreateNode(typeof(RootNode)) as RootNode;
            EditorUtility.SetDirty(tree);
            AssetDatabase.SaveAssets();
        }

        foreach (BaseNode n in tree.nodes)
        {
            CreateNodeView(n);
        }
        
        tree.nodes.ForEach(n =>
        {
            var children = tree.GetChildren(n);
            
            children.ForEach(c =>
            {
                NodeView parentNode =  FindNodeView(n);
                NodeView childNode =  FindNodeView(c);

                var edge =  parentNode.outputPort.ConnectTo(childNode.inputPort);
                AddElement(edge);
            });
        });
    }

    public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
    {
        return ports.ToList().Where(endport =>
            endport.direction != startPort.direction &&
            endport.node != startPort.node).ToList();
    }

    private GraphViewChange OnGraphViewChanged(GraphViewChange graphviewchange)
    {
        if (graphviewchange.elementsToRemove != null)
        {
            foreach (GraphElement element in graphviewchange.elementsToRemove)
            {
                NodeView nodeView = element as NodeView;

                if (nodeView != null)
                {
                    tree.DeleteNode(nodeView.node);
                }

                Edge edge = element as Edge;

                if (edge == null) continue;
                
                NodeView parentNodeView = edge.output.node as NodeView;
                NodeView childNodeView = edge.input.node as NodeView;
                
                tree.RemoveChild(parentNodeView.node, childNodeView.node);
            }
        }

        if (graphviewchange.edgesToCreate != null)
        {
            
            graphviewchange.edgesToCreate.ForEach(edge =>
            {
                NodeView parentNodeView = edge.output.node as NodeView;
                NodeView childNodeView = edge.input.node as NodeView;
                
                tree.AddChild(parentNodeView.node, childNodeView.node);
            });
        }

        if (graphviewchange.movedElements != null)
        {
            nodes.ForEach((n =>
            {
                NodeView nodeView = n as NodeView;
                nodeView.SortChildren();
            }));
        }


        return graphviewchange;
    }

    public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
    {
        //base.BuildContextualMenu(evt);
        {
            var types = TypeCache.GetTypesDerivedFrom<SelectorNode>();
            foreach (var type in types)
            {
                evt.menu.AppendAction($"[{type.BaseType.Name}] {type.Name}", (a) => CreateNode(type));
            }
        }
        
        {
            var types = TypeCache.GetTypesDerivedFrom<ActionNode>();
            foreach (var type in types)
            {
                evt.menu.AppendAction($"[{type.BaseType.Name}] {type.Name}", (a) => CreateNode(type));
            }
        }
        
        {
            var types = TypeCache.GetTypesDerivedFrom<CompositeNode>();
            foreach (var type in types)
            {
                evt.menu.AppendAction($"[{type.BaseType.Name}] {type.Name}", (a) => CreateNode(type));
            }
        }
        
        {
            var types = TypeCache.GetTypesDerivedFrom<DecoratorNode>();
            foreach (var type in types)
            {
                evt.menu.AppendAction($"[{type.BaseType.Name}] {type.Name}", (a) => CreateNode(type));
            }
        }
    }

    void CreateNode(System.Type type)
    {
        BaseNode node = tree.CreateNode(type);
        CreateNodeView(node);
    }


    void CreateNodeView(BaseNode node)
    {
        NodeView nodeView = new NodeView(node);
        nodeView.OnNodeSelected = OnNodeSelected;
        AddElement(nodeView);
    }

    public void UpdateRuntimeNodeState()
    {
        nodes.ForEach(n =>
        {
            NodeView view = n as NodeView;
            view.RuntimeUpdateState();
        });
    }
}
