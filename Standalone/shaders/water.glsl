// Animated water: vertex waves + two scrolling normal maps (Assets/_Game/Textures/Normals) + fresnel tint + sun specular.
// Material properties (see [material.water] in tunables.cfg): _WaveAmplitude, _WaveLength, _WaveSpeed, _Tiling, _ScrollSpeed,
// _NormalStrength, _ShallowColor, _DeepColor, _Fresnel, _Opacity, _Specular; textures _NormalA, _NormalB.

#pragma vertex
#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aUV;
uniform mat4 uViewProj;
uniform mat4 uModel;
uniform float uTime;
uniform float _WaveAmplitude;
uniform float _WaveLength;
uniform float _WaveSpeed;
out vec2 vUV;
out vec3 vWorldPos;
void main() {
    vec4 wp = uModel * vec4(aPos, 1.0);
    float k = 6.2831853 / max(_WaveLength, 0.01);
    float w = sin((wp.x * 0.7 + wp.z * 0.3) * k + uTime * _WaveSpeed) + 0.5 * sin((wp.z * 0.8 - wp.x * 0.2) * k * 1.7 + uTime * _WaveSpeed * 1.3);
    wp.y += w * _WaveAmplitude;
    vWorldPos = wp.xyz;
    vUV = aUV;
    gl_Position = uViewProj * wp;
}

#pragma fragment
#version 330 core
in vec2 vUV;
in vec3 vWorldPos;
uniform sampler2D uTex;
uniform sampler2D _NormalA;
uniform sampler2D _NormalB;
uniform vec4 uTint;
uniform float _Tiling;
uniform float _ScrollSpeed;
uniform float _NormalStrength;
uniform vec4 _ShallowColor;
uniform vec4 _DeepColor;
uniform float _Fresnel;
uniform float _Opacity;
uniform float _Specular;
#include "lighting.glsl"
out vec4 FragColor;
void main() {
    vec2 uv = vWorldPos.xz * _Tiling;
    vec3 n1 = texture(_NormalA, uv + vec2(uTime * _ScrollSpeed, uTime * _ScrollSpeed * 0.6)).xyz * 2.0 - 1.0;
    vec3 n2 = texture(_NormalB, uv * 1.7 - vec2(uTime * _ScrollSpeed * 0.8, -uTime * _ScrollSpeed * 0.5)).xyz * 2.0 - 1.0;
    vec3 n = normalize(vec3((n1.xy + n2.xy) * _NormalStrength, 1.0)).xzy; // tangent space of a flat plane: up = y
    n = normalize(vec3(n.x, abs(n.y) + 0.5, n.z));
    vec3 v = normalize(uCameraPos - vWorldPos);
    float fresnel = pow(1.0 - max(dot(n, v), 0.0), 3.0) * _Fresnel;
    vec3 base = mix(_DeepColor.rgb, _ShallowColor.rgb, fresnel) * uTint.rgb;
    vec3 lit = shade(base, n, vWorldPos, 0.0);
    vec3 h = normalize(v - uSunDir);
    lit += uSunColor * pow(max(dot(n, h), 0.0), 120.0) * _Specular * sampleShadow(vWorldPos, vec3(0.0, 1.0, 0.0));
    float alpha = clamp(_Opacity + fresnel * 0.5, 0.0, 1.0);
    FragColor = vec4(applyFog(lit, vWorldPos), alpha);
}
