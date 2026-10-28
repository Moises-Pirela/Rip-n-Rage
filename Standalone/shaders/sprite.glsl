// Default sprite shader (engine built-in "sprite"): world sprites (lit, shadowed, fogged) and first-person sprites (uLit = 0).
// Engine uniforms: uViewProj, uModel, uTex, uTint, uAlphaCutoff, uLit, uSpriteNormal, uWrap, uTime + lighting.glsl set.
// Material/property-block properties: _Flash (0..1), _FlashColor.
#pragma vertex
#version 330 core
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
}

#pragma fragment
#version 330 core
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
#include "lighting.glsl"
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
}
