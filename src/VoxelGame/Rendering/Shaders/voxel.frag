#version 450

layout(location = 0) in vec3 fragNormal;
layout(location = 1) in vec2 fragUv;
layout(location = 2) flat in uint fragBlockType;
layout(location = 3) in vec3 fragTint;
layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 1) uniform sampler2D blockAtlas;

void main()
{
    vec4 texel = texture(blockAtlas, fragUv);
    if (fragBlockType == 100)
    {
        float crack = 1.0 - texel.r;
        outColor = vec4(vec3(0.02), crack * 0.85);
        return;
    }

    vec3 lightDir = normalize(vec3(0.35, 0.85, 0.25));
    float light = max(dot(normalize(fragNormal), lightDir), 0.18);
    vec3 color = texel.rgb * fragTint * light;

    if (fragBlockType == 7 || fragBlockType == 8)
    {
        color += texel.rgb * 0.25;
    }

    float alpha = fragBlockType == 5 ? 0.72 * texel.a : texel.a;
    outColor = vec4(color, alpha);
}
