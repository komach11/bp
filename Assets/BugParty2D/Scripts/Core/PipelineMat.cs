using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// 渲染管线适配层。解决「同一份代码要在 Built-in 与 URP 两个工程里都正常显示」的问题。
    ///
    /// 【问题根源】
    /// GameObject.CreatePrimitive() 生成的物体自带 Built-in 的 Standard 材质。
    /// URP 工程里 Standard shader 不存在 → 物体渲染为洋红（magenta）。
    /// 而 new Material(r.sharedMaterial) 只是复制那个坏材质，改颜色也救不回来。
    ///
    /// 本地密室工程是 Built-in、内网 minigame 工程是 URP，两边共用同一份代码，
    /// 所以不能硬编码任一 shader —— 必须运行时探测。
    ///
    /// 【为什么把 Sprites/Default 作为兜底】
    /// 它在 Built-in 与 URP 下都内置可用，自带 alpha 混合，无需手动配置混合状态
    /// 与 shader 关键字。内网 PartyGame 的 HookShotTracer / WaterReloadBar 等
    /// 已经在用这个约定，跟着走比自己拼 keyword 稳得多。
    ///
    /// 【用法】
    /// CreatePrimitive 之后要设颜色的地方，一律走 PipelineMat.Apply(renderer, color)，
    /// 不要再自己 new Material(r.sharedMaterial)。
    /// </summary>
    public static class PipelineMat
    {
        static Shader _lit;
        static Shader _unlit;
        static bool _resolved;

        /// <summary>当前工程是否跑在 SRP（URP/HDRP）下。</summary>
        public static bool IsSRP =>
            UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;

        static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            // 受光材质：URP Lit → Built-in Standard → 兜底 Sprites/Default
            _lit = Shader.Find("Universal Render Pipeline/Lit");
            if (_lit == null) _lit = Shader.Find("Standard");
            if (_lit == null) _lit = Shader.Find("Sprites/Default");

            // 不受光材质：阴影片、UI 条、特效等不需要打光的东西。
            // Sprites/Default 两管线通用且自带透明，作为主选而非兜底
            _unlit = Shader.Find("Sprites/Default");
            if (_unlit == null) _unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (_unlit == null) _unlit = Shader.Find("Unlit/Color");
            if (_unlit == null) _unlit = _lit;

            if (_lit == null)
                Debug.LogWarning("[PipelineMat] 未找到任何可用 shader，材质会显示异常");
        }

        /// <summary>受光 shader。URP 下是 Universal Render Pipeline/Lit，Built-in 下是 Standard。</summary>
        public static Shader Lit { get { Resolve(); return _lit; } }

        /// <summary>不受光 shader（两管线通用）。</summary>
        public static Shader Unlit { get { Resolve(); return _unlit; } }

        /// <summary>
        /// 给 Renderer 换上当前管线可用的材质并设色。
        ///
        /// 关键点：不复用 r.sharedMaterial —— 那可能正是坏掉的 Standard，
        /// 而是用探测到的 shader 全新创建。
        ///
        /// unlit = true 时用不受光材质（阴影、进度条、特效）。
        /// 半透明（a &lt; 1）会自动改用 Sprites/Default，它自带 alpha 混合。
        /// </summary>
        public static Material Apply(Renderer r, Color c, bool unlit = false)
        {
            if (r == null) return null;
            Resolve();

            // 半透明一律走 Unlit(Sprites/Default)：URP Lit 要开透明得改 _Surface
            // 与一堆 shader 关键字，容易在不同 URP 版本上失效
            bool needsAlpha = c.a < 0.999f;
            var sh = (unlit || needsAlpha) ? _unlit : _lit;
            if (sh == null) return r.sharedMaterial;

            var mat = new Material(sh);
            mat.name = "Mat_" + r.gameObject.name;
            SetColorOn(mat, c);

            r.sharedMaterial = mat;
            return mat;
        }

        /// <summary>只改颜色，不换 shader。用于已经是正确材质的情况（如美术模型）。</summary>
        public static void SetColorOn(Material mat, Color c)
        {
            if (mat == null) return;
            // 两个属性名都写：URP Lit 用 _BaseColor，Built-in Standard 与
            // Sprites/Default 用 _Color
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        }

        /// <summary>
        /// 判断某个 Renderer 的 shader 在当前管线下是否已失效（会显示洋红）。
        /// </summary>
        public static bool IsBroken(Renderer r)
        {
            if (r == null) return false;
            var mat = r.sharedMaterial;
            if (mat == null) return true;

            var sh = mat.shader;
            if (sh == null) return true;
            if (sh.name == "Hidden/InternalErrorShader") return true;
            if (!sh.isSupported) return true;

            // URP 下 Built-in 的 Standard/Legacy 系列虽然 isSupported 可能为 true，
            // 实际渲染仍是洋红，所以按名字额外判一次
            if (IsSRP)
            {
                if (sh.name == "Standard") return true;
                if (sh.name.StartsWith("Legacy Shaders/")) return true;
            }
            return false;
        }

        /// <summary>
        /// 修补一个已存在的 Renderer：shader 失效时换成可用的并尽量保留原色。
        /// 用于修已经建好的场景，不必重新建场。返回是否做了修补。
        /// </summary>
        public static bool RepairIfBroken(Renderer r)
        {
            if (!IsBroken(r)) return false;

            var c = Color.white;
            var mat = r.sharedMaterial;
            if (mat != null)
            {
                if (mat.HasProperty("_BaseColor")) c = mat.GetColor("_BaseColor");
                else if (mat.HasProperty("_Color")) c = mat.GetColor("_Color");
            }

            Apply(r, c);
            return true;
        }
    }
}
