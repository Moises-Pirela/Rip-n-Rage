#if UNITY_EDITOR
 using Sirenix.OdinInspector.Editor;
    using Sirenix.Utilities;
    using Sirenix.Utilities.Editor;
    using Sirenix.OdinInspector.Demos.RPGEditor;
    using UnityEditor;
    using UnityEngine;
    using System.Linq;

public class WeaponEditor : OdinMenuEditorWindow
{
    [MenuItem("Tools/Weapon Editor")]
        private static void Open()
        {
            var window = GetWindow<WeaponEditor>();
            window.position = GUIHelper.GetEditorWindowRect().AlignCenter(800, 500);
        }

        protected override OdinMenuTree BuildMenuTree()
        {
            var tree = new OdinMenuTree(true);
            tree.DefaultMenuStyle.IconSize = 28.00f;
            tree.Config.DrawSearchToolbar = true;

            // Adds the character overview table.
            tree.Add("Weapons", null);

            // Adds all characters.
            tree.AddAllAssetsAtPath("Weapons", "Assets/_Game/ScriptableObjects/WeaponData", typeof(WeaponData), true, true);

            return tree;
        }

         protected override void OnBeginDrawEditors()
        {
            var selected = this.MenuTree.Selection.FirstOrDefault();
            var toolbarHeight = this.MenuTree.Config.SearchToolbarHeight;

            // Draws a toolbar with the name of the currently selected menu item.
            SirenixEditorGUI.BeginHorizontalToolbar(toolbarHeight);
            {
                if (selected != null)
                {
                    GUILayout.Label(selected.Name);
                }

                if (SirenixEditorGUI.ToolbarButton(new GUIContent("Create Weapon")))
                {
                    ScriptableObjectCreator.ShowDialog<WeaponData>("Assets/_Game/ScriptableObjects/WeaponData", obj =>
                    {
                        obj.WeaponName = obj.name;
                        base.TrySelectMenuItemWithObject(obj); // Selects the newly created item in the editor
                    });
                }
            SirenixEditorGUI.EndHorizontalToolbar();
        }
    }
}
#endif
