// Shared lighting for every lit surface shader (meshes and sprites).
// Provided uniforms are set by the engine every frame; call shade()/applyFog()/sampleShadow().

uniform vec3 uSunDir;        // direction the sunlight travels
uniform vec3 uSunColor;      // colour * intensity
uniform vec3 uAmbientSky;
uniform vec3 uAmbientGround;
uniform int uLightCount;
uniform vec3 uLightPos[16];
uniform vec3 uLightColor[16];
uniform float uLightRange[16];
uniform sampler2D uShadowMap;
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
    float s = 0.0;
    for (int x = -1; x <= 1; x++)
        for (int y = -1; y <= 1; y++) {
            float d = texture(uShadowMap, p.xy + vec2(float(x), float(y)) * uShadowTexel * uShadowSoftness).r;
            s += (p.z - uShadowBias <= d) ? 1.0 : 0.0;
        }
    s /= 9.0;
    vec2 e = abs(p.xy - 0.5) * 2.0;
    float fade = smoothstep(0.85, 1.0, max(e.x, e.y));
    return mix(s, 1.0, fade);
}

vec3 shade(vec3 albedo, vec3 n, vec3 wp, float wrap) {
    vec3 ambient = mix(uAmbientGround, uAmbientSky, n.y * 0.5 + 0.5);
    float ndl = clamp((dot(n, -uSunDir) + wrap) / (1.0 + wrap), 0.0, 1.0);
    float shadow = sampleShadow(wp, n);
    vec3 col = albedo * (ambient + uSunColor * ndl * shadow);
    vec3 v = normalize(uCameraPos - wp);
    if (uSpecular > 0.0 && ndl > 0.0) {
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
