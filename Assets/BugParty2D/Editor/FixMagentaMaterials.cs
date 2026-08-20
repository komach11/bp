using UnityEditor;
using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// 一键修复场景里因渲染管线不匹配而显示洋红的材质。
    ///
    /// 【什么时候用】
    /// 场景是在 Built-in 工程里建的，拿到 URP 工程后打开发现满屋洋红 ——
    /// 那些 primitive 的材质用的是 Built-in Standard shader，URP 下不存在。
    ///
    /// 直接重新建场也能解决（建场代码已改为走 PipelineMat），但如果场景里有
    /// 手工调整过的内容不想丢，用这个工具原地修补更稳妥。
    /// </summary>
    public static class FixMagentaMaterials
    {
        [MenuItem("BugParty2D/修复洋红材质（管线不匹配）", priority = 200)]
        [MenuItem("Tools/BugParty2D/修复洋红材质（管线不匹配）", priority = 200)]
        public static void Fix()
        {
            bool srp = PipelineMat.IsSRP;
            string pipeline = srp
                ? (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                    ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.GetType().Name
                    : "SRP")
                : "Built-in";

            var all = Object.FindObjectsOfType<Renderer>(true);
            int checkedCount = 0, fixedCount = 0;

            Undo.RecordObjects(all, "Fix Magenta Materials");

            for (int i = 0; i < all.Length; i++)
            {
                var r = all[i];
                if (r == null) continue;

                // 只处理程序化生成的物件（材质名以 Mat_ 开头或直接是内置默认材质），
                // 不动美术导入的模型材质 —— 那些可能是刻意配置的
                var mat = r.sharedMaterial;
                if (mat == null) continue;

                bool isProcedural =
                    mat.name.StartsWith("Mat_") ||
                    mat.name.StartsWith("Default-") ||
                    mat.name == "Lit" || mat.name == "Standard";

                if (!isProcedural && !PipelineMat.IsBroken(r)) continue;

                checkedCount++;
                if (PipelineMat.RepairIfBroken(r))
                {
                    fixedCount++;
                    EditorUtility.SetDirty(r);
                }
            }

            if (fixedCount > 0)
            {
                EditorSceneManagerMarkDirty();
                Debug.Log($"[修复洋红] 当前管线 {pipeline}｜检查 {checkedCount} 个 Renderer，" +
                          $"修复 {fixedCount} 个。记得保存场景。");
                EditorUtility.DisplayDialog("修复完成",
                    $"当前管线：{pipeline}\n\n" +
                    $"检查了 {checkedCount} 个渲染器\n修复了 {fixedCount} 个洋红材质\n\n" +
                    "记得 Ctrl+S 保存场景。", "好");
            }
            else
            {
                Debug.Log($"[修复洋红] 当前管线 {pipeline}｜检查 {checkedCount} 个 Renderer，" +
                          "没有发现需要修复的材质。");
                EditorUtility.DisplayDialog("无需修复",
                    $"当前管线：{pipeline}\n\n" +
                    $"检查了 {checkedCount} 个渲染器，未发现失效材质。\n\n" +
                    "如果画面仍是洋红，可能是美术模型自带的材质问题，\n" +
                    "可用 Edit ▸ Rendering ▸ Materials ▸ Convert Selected\n" +
                    "Built-in Materials to URP 转换。", "好");
            }
        }

        static void EditorSceneManagerMarkDirty()
        {
            // GetActiveScene 属于 UnityEngine 的 SceneManager，不是 EditorSceneManager
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.IsValid())
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
