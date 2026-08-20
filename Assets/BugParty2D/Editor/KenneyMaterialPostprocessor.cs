using UnityEditor;
using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// Kenney 等 OBJ 素材的导入后处理：把 obj/mtl 自带的 Built-in 材质
    /// 替换成当前管线可用的 shader。
    ///
    /// 【为什么需要】
    /// Kenney 的 .mtl 只有 Ka/Kd/Ks 这些经典参数，Unity 导入时会创建
    /// Built-in Standard 材质。在 URP 工程里 Standard shader 不存在 →
    /// 模型全部显示洋红。
    ///
    /// Unity 官方的 Edit ▸ Rendering ▸ Materials ▸ Convert 也能转，但它是
    /// 手动一次性的；这个处理器让**每次重新导入都自动生效**，团队成员
    /// 拉下工程后不需要额外操作。
    ///
    /// 只处理 BugParty2D/Art 下的资源，不干扰内网工程其他美术资产。
    /// </summary>
    public class KenneyMaterialPostprocessor : AssetPostprocessor
    {
        const string ArtRoot = "Assets/BugParty2D/Art/";

        /// <summary>
        /// OBJ 导入时 Unity 会为每个 mtl 条目调这个回调，返回的材质会被使用。
        /// 返回 null 表示走 Unity 默认流程。
        /// </summary>
        Material OnAssignMaterialModel(Material material, Renderer renderer)
        {
            if (!assetPath.StartsWith(ArtRoot)) return null;

            // 当前工程是 Built-in 的话 Unity 默认行为就是对的，不必插手
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null)
                return null;

            var urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null) return null;

            // 保留 mtl 里解析出来的漫反射色，只换 shader
            var baseColor = Color.white;
            if (material != null)
            {
                if (material.HasProperty("_Color")) baseColor = material.GetColor("_Color");
                else if (material.HasProperty("_BaseColor")) baseColor = material.GetColor("_BaseColor");
            }

            var mat = new Material(urpLit);
            mat.name = material != null ? material.name : "KenneyMat";
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", baseColor);

            // low-poly 风格不需要高光，关掉更接近原始观感
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.1f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);

            return mat;
        }
    }
}
