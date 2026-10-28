// Sprite dissolve (death effect): cuts the sprite away with animated noise and glows at the edge.
// Properties: _Dissolve (0 = intact, 1 = gone), _EdgeWidth, _EdgeColor, _NoiseScale.

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
uniform float _Dissolve;
uniform float _EdgeWidth;
uniform vec4 _EdgeColor;
uniform float _NoiseScale;
#include "lighting.glsl"
out vec4 FragColor;
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y);
}
void main() {
    vec4 c = texture(uTex, vUV) * uTint;
    if (c.a < uAlphaCutoff) discard;
    float scale = _NoiseScale > 0.0 ? _NoiseScale : 24.0;
    float n = noise(vUV * scale) * 0.6 + noise(vUV * scale * 2.3) * 0.4;
    float cut = _Dissolve * 1.1;
    if (n < cut) discard;
    float edge = _EdgeWidth > 0.0 ? _EdgeWidth : 0.08;
    float glow = 1.0 - smoothstep(cut, cut + edge, n);
    if (uLit == 1) c.rgb = applyFog(shade(c.rgb, normalize(uSpriteNormal), vWorldPos, uWrap), vWorldPos);
    vec3 edgeColor = _EdgeColor.a > 0.0 ? _EdgeColor.rgb : vec3(1.0, 0.45, 0.1);
    c.rgb = mix(c.rgb, edgeColor * 2.0, glow);
    FragColor = c;
}
