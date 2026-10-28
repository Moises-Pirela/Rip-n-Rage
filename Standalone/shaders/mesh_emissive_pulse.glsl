// Lit mesh with a pulsing emissive term (lamp heads). Properties: _PulseSpeed, _PulseMin, _PulseMax, _EmissiveColor.

#pragma vertex
#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aUV;
layout(location = 3) in vec4 aColor;   // vertex paint (white when unpainted)
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
}

#pragma fragment
#version 330 core
in vec3 vNormal;
in vec2 vUV;
in vec3 vWorldPos;
in vec4 vColor;
uniform sampler2D uTex;
uniform vec4 uTint;
uniform vec2 uUVScale;
uniform float uEmissive;
uniform float _PulseSpeed;
uniform float _PulseMin;
uniform float _PulseMax;
uniform vec4 _EmissiveColor;
#include "lighting.glsl"
out vec4 FragColor;
void main() {
    vec3 n = normalize(vNormal);
    vec4 albedo = texture(uTex, vUV * uUVScale) * uTint * vColor;
    float pulse = mix(_PulseMin, _PulseMax, 0.5 + 0.5 * sin(uTime * _PulseSpeed + vWorldPos.x * 0.37 + vWorldPos.z * 0.21));
    vec3 emissive = (_EmissiveColor.a > 0.0 ? _EmissiveColor.rgb : albedo.rgb) * (uEmissive + pulse);
    vec3 lit = shade(albedo.rgb, n, vWorldPos, 0.0) + emissive;
    FragColor = vec4(applyFog(lit, vWorldPos), albedo.a);
}
