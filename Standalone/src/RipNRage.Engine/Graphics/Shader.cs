using System;
using System.Collections.Generic;
using RipNRage.Engine.Platform;

namespace RipNRage.Engine.Graphics
{
    /// <summary>Compiled GLSL program with cached uniform locations.</summary>
    public sealed class Shader : IDisposable
    {
        public uint Program { get; private set; }
        public string Name { get; }
        /// <summary>Incremented on every (re)compile so caches keyed on the shader can invalidate.</summary>
        public int Version { get; private set; }
        private readonly Dictionary<string, int> _uniforms = new Dictionary<string, int>();

        public Shader(string vertexSource, string fragmentSource, string name = "shader")
        {
            Name = name;
            Program = Build(vertexSource, fragmentSource, name);
        }

        /// <summary>Replaces the program in place (hot reload). Throws and keeps the old program on failure.</summary>
        public void Recompile(string vertexSource, string fragmentSource)
        {
            uint fresh = Build(vertexSource, fragmentSource, Name);
            if (Program != 0) GL.DeleteProgram(Program);
            Program = fresh;
            _uniforms.Clear();
            Version++;
        }

        private static uint Build(string vertexSource, string fragmentSource, string name)
        {
            uint vs = Compile(GL.VERTEX_SHADER, vertexSource, name + ".vert");
            uint fs;
            try { fs = Compile(GL.FRAGMENT_SHADER, fragmentSource, name + ".frag"); }
            catch { GL.DeleteShader(vs); throw; }
            uint program = GL.CreateProgram();
            GL.AttachShader(program, vs);
            GL.AttachShader(program, fs);
            GL.LinkProgram(program);
            GL.DeleteShader(vs);
            GL.DeleteShader(fs);
            if (GL.GetProgrami(program, GL.LINK_STATUS) == 0)
            {
                string log = GL.GetProgramInfoLog(program);
                GL.DeleteProgram(program);
                throw new InvalidOperationException($"Shader link failed ({name}): {log}");
            }
            return program;
        }

        private static uint Compile(uint type, string src, string name)
        {
            uint s = GL.CreateShader(type);
            GL.ShaderSource(s, src);
            GL.CompileShader(s);
            if (GL.GetShaderi(s, GL.COMPILE_STATUS) == 0)
                throw new InvalidOperationException($"Shader compile failed ({name}): {GL.GetShaderInfoLog(s)}");
            return s;
        }

        public void Use() => GL.UseProgram(Program);

        public int Loc(string uniform)
        {
            if (!_uniforms.TryGetValue(uniform, out int loc))
            {
                loc = GL.GetUniformLocation(Program, uniform);
                _uniforms[uniform] = loc;
            }
            return loc;
        }

        public void SetMatrix(string n, Matrix4x4 m) => GL.UniformMatrix4(Loc(n), m);
        public void SetInt(string n, int v) => GL.Uniform1i(Loc(n), v);
        public void SetFloat(string n, float v) => GL.Uniform1f(Loc(n), v);
        public void SetVector(string n, Vector2 v) => GL.Uniform2f(Loc(n), v.x, v.y);
        public void SetVector(string n, Vector3 v) => GL.Uniform3f(Loc(n), v.x, v.y, v.z);
        public void SetColor(string n, Color c) => GL.Uniform4f(Loc(n), c.r, c.g, c.b, c.a);
        public void SetVector4(string n, float x, float y, float z, float w) => GL.Uniform4f(Loc(n), x, y, z, w);

        public unsafe void SetVector3Array(string n, Vector3[] values, int count)
        {
            if (count <= 0) return;
            float* buf = stackalloc float[count * 3];
            for (int i = 0; i < count; i++) { buf[i * 3] = values[i].x; buf[i * 3 + 1] = values[i].y; buf[i * 3 + 2] = values[i].z; }
            GL.Uniform3fv(Loc(n), count, buf);
        }

        public unsafe void SetFloatArray(string n, float[] values, int count)
        {
            if (count <= 0) return;
            fixed (float* p = values) GL.Uniform1fv(Loc(n), count, p);
        }

        public void Dispose()
        {
            if (Program != 0) { GL.DeleteProgram(Program); Program = 0; }
        }
    }

    /// <summary>Built-in GLSL 330 shaders (embedded fallbacks for the files in shaders/).</summary>
    public static class BuiltinShaders
    {
        public const string ErrorVert = @"#version 330 core
layout(location = 0) in vec3 aPos; uniform mat4 uViewProj; uniform mat4 uModel;
void main() { gl_Position = uViewProj * uModel * vec4(aPos, 1.0); }";
        public const string ErrorFrag = @"#version 330 core
out vec4 FragColor; void main() { FragColor = vec4(1.0, 0.0, 1.0, 1.0); }";

        /// <summary>GLSL shared by every lit surface shader: sun + hemisphere ambient + point lights + PCF shadows + distance fog.</summary>
        public const string LightingCommon = @"
// Shared lighting for every lit surface shader (meshes and sprites).
// Provided uniforms are set by the engine every frame; call shade()/applyFog()/sampleShadow().
// The shadow map is a depth texture in hardware compare mode (sampler2DShadow): one fetch returns a
// bilinearly filtered 0..1 visibility, so 1 tap (softness 0) or 4 taps replace a 9-tap manual PCF.
// uLightCount / uLightPos... hold only the point lights that touch the object being drawn (per-draw culling).

uniform vec3 uSunDir;        // direction the sunlight travels
uniform vec3 uSunColor;      // colour * intensity
uniform vec3 uAmbientSky;
uniform vec3 uAmbientGround;
uniform int uLightCount;
uniform vec3 uLightPos[16];
uniform vec3 uLightColor[16];
uniform float uLightRange[16];
uniform sampler2DShadow uShadowMap;
uniform mat4 uShadowMatrix;
uniform int uShadowsOn;
uniform float uShadowBias;
uniform float uShadowNormalOffset;
uniform float uShadowSoftness;
uniform float uShadowTexel;
uniform float uSpecular;
uniform vec3 uCameraPos;
uniform vec3 uFogColor;
uniform vec2 uFogRange;      // start, end (end <= start disables)
uniform float uTime;         // seconds since start
uniform vec4 uTimeVec;       // time, sin(time), cos(time), deltaTime

float sampleShadow(vec3 wp, vec3 n) {
    if (uShadowsOn == 0) return 1.0;
    vec4 sc = uShadowMatrix * vec4(wp + n * uShadowNormalOffset, 1.0);
    vec3 p = sc.xyz / sc.w;
    if (p.x < 0.0 || p.x > 1.0 || p.y < 0.0 || p.y > 1.0 || p.z > 1.0) return 1.0;
    float z = p.z - uShadowBias;
    float s;
    if (uShadowSoftness <= 0.0) {
        s = texture(uShadowMap, vec3(p.xy, z));
    } else {
        float o = uShadowTexel * uShadowSoftness * 0.75;
        s = texture(uShadowMap, vec3(p.xy + vec2(-o, -o), z))
          + texture(uShadowMap, vec3(p.xy + vec2( o, -o), z))
          + texture(uShadowMap, vec3(p.xy + vec2(-o,  o), z))
          + texture(uShadowMap, vec3(p.xy + vec2( o,  o), z));
        s *= 0.25;
    }
    vec2 e = abs(p.xy - 0.5) * 2.0;
    float fade = smoothstep(0.85, 1.0, max(e.x, e.y));
    return mix(s, 1.0, fade);
}

vec3 shade(vec3 albedo, vec3 n, vec3 wp, float wrap) {
    vec3 ambient = mix(uAmbientGround, uAmbientSky, n.y * 0.5 + 0.5);
    float ndl = clamp((dot(n, -uSunDir) + wrap) / (1.0 + wrap), 0.0, 1.0);
    float shadow = ndl > 0.0 ? sampleShadow(wp, n) : 1.0;   // fully back-facing texels need no shadow fetch
    vec3 col = albedo * (ambient + uSunColor * ndl * shadow);
    if (uSpecular > 0.0 && ndl > 0.0) {
        vec3 v = normalize(uCameraPos - wp);
        vec3 h = normalize(v - uSunDir);
        col += uSunColor * shadow * pow(max(dot(n, h), 0.0), 32.0) * uSpecular * ndl;
    }
    for (int i = 0; i < uLightCount; i++) {
        vec3 d = uLightPos[i] - wp;
        float dist = length(d);
        if (dist >= uLightRange[i]) continue;
        float att = 1.0 - dist / uLightRange[i];
        att = att * att;
        float l = clamp((dot(n, d / dist) + wrap) / (1.0 + wrap), 0.0, 1.0);
        col += albedo * uLightColor[i] * att * l;
    }
    return col;
}

vec3 applyFog(vec3 col, vec3 wp) {
    if (uFogRange.y <= uFogRange.x) return col;
    float dist = distance(wp, uCameraPos);
    float fog = clamp((dist - uFogRange.x) / (uFogRange.y - uFogRange.x), 0.0, 1.0);
    return mix(col, uFogColor, fog);
}
";

        public const string SpriteVert = @"#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec2 aUV;
uniform mat4 uViewProj;
uniform mat4 uModel;
out vec2 vUV;
out vec3 vWorldPos;
void main() {
    vUV = aUV;
    vec4 wp = uModel * vec4(aPos, 1.0);
    vWorldPos = wp.xyz;
    gl_Position = uViewProj * wp;
}";

        public const string SpriteFrag = @"#version 330 core
in vec2 vUV;
in vec3 vWorldPos;
uniform sampler2D uTex;
uniform vec4 uTint;
uniform float uAlphaCutoff;
uniform int uLit;
uniform vec3 uSpriteNormal;
uniform float uWrap;
uniform float _Flash;        // 0..1 white hit-flash (per-object property block)
uniform vec4 _FlashColor;
" + LightingCommon + @"
out vec4 FragColor;
void main() {
    vec4 c = texture(uTex, vUV) * uTint;
    if (c.a < uAlphaCutoff) discard;
    if (uLit == 1) {
        vec3 lit = shade(c.rgb, normalize(uSpriteNormal), vWorldPos, uWrap);
        c.rgb = applyFog(lit, vWorldPos);
    }
    vec3 flashColor = _FlashColor.a > 0.0 ? _FlashColor.rgb : vec3(1.0);
    c.rgb = mix(c.rgb, flashColor, clamp(_Flash, 0.0, 1.0));
    FragColor = c;
}";

        /// <summary>Particle batches (ParticleRenderer): world-space quads with per-vertex colour; optional lighting/fog.</summary>
        public const string ParticleVert = @"#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec2 aUV;
layout(location = 2) in vec4 aColor;
uniform mat4 uViewProj;
out vec2 vUV;
out vec4 vColor;
out vec3 vWorldPos;
void main() {
    vUV = aUV;
    vColor = aColor;
    vWorldPos = aPos;
    gl_Position = uViewProj * vec4(aPos, 1.0);
}";

        public const string ParticleFrag = @"#version 330 core
in vec2 vUV;
in vec4 vColor;
in vec3 vWorldPos;
uniform sampler2D uTex;
uniform int uLit;
uniform int uMultiply;
" + LightingCommon + @"
out vec4 FragColor;
void main() {
    vec4 tex = texture(uTex, vUV);
    vec4 c = tex * vColor;
    if (c.a < 0.004) discard;
    if (uLit == 1) {
        vec3 lit = shade(c.rgb, vec3(0.0, 1.0, 0.0), vWorldPos, 1.0);
        c.rgb = applyFog(lit, vWorldPos);
    }
    if (uMultiply == 1) c.rgb = mix(vec3(1.0), c.rgb, c.a); // multiply blend: alpha fades towards no darkening
    FragColor = c;
}";

        public const string MeshVert = @"#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aUV;
layout(location = 3) in vec4 aColor;
uniform mat4 uViewProj;
uniform mat4 uModel;
out vec3 vNormal;
out vec2 vUV;
out vec3 vWorldPos;
out vec4 vColor;
void main() {
    vec4 wp = uModel * vec4(aPos, 1.0);
    vWorldPos = wp.xyz;
    vNormal = mat3(uModel) * aNormal;
    vUV = aUV;
    vColor = aColor;
    gl_Position = uViewProj * wp;
}";

        public const string MeshFrag = @"#version 330 core
in vec3 vNormal;
in vec2 vUV;
in vec3 vWorldPos;
in vec4 vColor;
uniform sampler2D uTex;
uniform vec4 uTint;
uniform vec2 uUVScale;
uniform float uEmissive;
uniform int uDebugShadow;
" + LightingCommon + @"
out vec4 FragColor;
void main() {
    vec3 n = normalize(vNormal);
    if (uDebugShadow == 1) { FragColor = vec4(vec3(sampleShadow(vWorldPos, n)), 1.0); return; }
    vec4 albedo = texture(uTex, vUV * uUVScale) * uTint * vColor; // vColor = vertex paint (white when unpainted)
    vec3 lit = shade(albedo.rgb, n, vWorldPos, 0.0) + albedo.rgb * uEmissive;
    FragColor = vec4(applyFog(lit, vWorldPos), albedo.a);
}";

        public const string UiVert = @"#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 aUV;
layout(location = 2) in vec4 aColor;
uniform mat4 uProj;
out vec2 vUV;
out vec4 vColor;
void main() {
    vUV = aUV;
    vColor = aColor;
    gl_Position = uProj * vec4(aPos, 0.0, 1.0);
}";

        public const string UiFrag = @"#version 330 core
in vec2 vUV;
in vec4 vColor;
uniform sampler2D uTex;
out vec4 FragColor;
void main() {
    FragColor = texture(uTex, vUV) * vColor;
}";

        public const string SkyVert = @"#version 330 core
layout(location = 0) in vec2 aPos;
out vec2 vPos;
void main() { vPos = aPos; gl_Position = vec4(aPos, 0.999999, 1.0); }";

        public const string SkyFrag = @"#version 330 core
in vec2 vPos;
uniform mat4 uInvViewProj;
uniform vec3 uHorizon;
uniform vec3 uZenith;
uniform vec3 uGround;
uniform vec3 uSunDir;
out vec4 FragColor;
void main() {
    vec4 near = uInvViewProj * vec4(vPos, -1.0, 1.0);
    vec4 far = uInvViewProj * vec4(vPos, 1.0, 1.0);
    vec3 dir = normalize(far.xyz / far.w - near.xyz / near.w);
    float t = clamp(dir.y, -1.0, 1.0);
    vec3 col = t >= 0.0 ? mix(uHorizon, uZenith, pow(t, 0.6)) : mix(uHorizon, uGround, pow(-t, 0.5));
    float sun = pow(max(dot(dir, -normalize(uSunDir)), 0.0), 256.0);
    col += vec3(1.0, 0.95, 0.8) * sun * 0.8;
    FragColor = vec4(col, 1.0);
}";
    }
}
