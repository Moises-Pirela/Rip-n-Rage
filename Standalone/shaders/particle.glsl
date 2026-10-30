// Particle batches (engine built-in "particle", used by ParticleRenderer for every data-driven VFX).
// Vertices come pre-transformed in world space with a per-vertex colour (tint × alpha over life).
// Engine uniforms: uViewProj, uTex, uLit (shade with the lighting set, normal up), uMultiply (multiply blend) + lighting.glsl set.
#pragma vertex
#version 330 core
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
}

#pragma fragment
#version 330 core
in vec2 vUV;
in vec4 vColor;
in vec3 vWorldPos;
uniform sampler2D uTex;
uniform int uLit;
uniform int uMultiply;
#include "lighting.glsl"
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
}
