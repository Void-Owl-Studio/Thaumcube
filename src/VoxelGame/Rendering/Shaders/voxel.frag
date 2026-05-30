#version 450

layout(location = 0) in vec3 fragNormal;
layout(location = 1) in vec2 fragUv;
layout(location = 2) flat in uint fragBlockType;
layout(location = 3) in vec3 fragTint;
layout(location = 4) in vec3 fragWorldPosition;
layout(location = 5) in float fragAlpha;
layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform CameraUniform
{
    mat4 view;
    mat4 projection;
    vec4 cameraPosition;
    vec4 fogColor;
    vec4 skyLightColor;
    vec4 fogSettings;
    vec4 lightDirection;
    vec4 cloudColor;
    vec4 cloudMotion;
    vec4 cloudOffset;
} camera;

layout(set = 0, binding = 1) uniform sampler2D blockAtlas;
layout(set = 0, binding = 2) uniform sampler2D environmentAtlas;
layout(set = 0, binding = 3) uniform sampler2D menuAtlas;

float ComputeFogFactor(vec3 worldPosition)
{
    float distanceToCamera = distance(worldPosition, camera.cameraPosition.xyz);
    float fogFactor = smoothstep(camera.fogSettings.x, camera.fogSettings.y, distanceToCamera);
    fogFactor *= exp2(-max(worldPosition.y - camera.cameraPosition.y, 0.0) * camera.fogSettings.z);
    return clamp(fogFactor, 0.0, 1.0);
}

void main()
{
    bool isCloud = fragBlockType == 200;
    bool isSun = fragBlockType == 201;
    bool isMenuBackground = fragBlockType == 210;
    bool isMenuLogo = fragBlockType == 211;
    vec4 texel;
    if (isMenuBackground || isMenuLogo)
    {
        texel = texture(menuAtlas, fragUv);
    }
    else if (isCloud || isSun)
    {
        texel = isCloud
            ? vec4(1.0)
            : texture(environmentAtlas, fragUv);
    }
    else
    {
        texel = texture(blockAtlas, fragUv);
    }

    if (texel.a <= 0.01)
    {
        discard;
    }

    if (fragBlockType == 100)
    {
        float crackMask = dot(texel.rgb, vec3(0.33333334));
        float crackAlpha = texel.a * mix(0.45, 0.90, crackMask);
        vec3 crackColor = vec3(0.06);
        outColor = vec4(crackColor, crackAlpha);
        return;
    }

    if (isSun)
    {
        outColor = vec4(texel.rgb * 1.2, texel.a);
        return;
    }

    if (isMenuBackground || isMenuLogo)
    {
        outColor = vec4(texel.rgb, texel.a);
        return;
    }

    if (isCloud)
    {
        vec3 normal = normalize(fragNormal);
        float topLight = clamp(normal.y * 0.5 + 0.5, 0.0, 1.0);
        float rim = pow(1.0 - clamp(dot(normalize(camera.cameraPosition.xyz - fragWorldPosition), normal), 0.0, 1.0), 1.5);
        float shading = mix(0.78, 1.08, topLight) + rim * 0.08;
        vec3 cloudColor = fragTint * camera.cloudColor.rgb * shading;
        float alpha = clamp(fragAlpha * camera.cloudColor.a, 0.0, 1.0);
        float fogFactor = ComputeFogFactor(fragWorldPosition);
        cloudColor = mix(cloudColor, camera.fogColor.xyz, fogFactor);
        alpha *= 1.0 - fogFactor;
        outColor = vec4(cloudColor, alpha);
        return;
    }

    vec3 normal = normalize(fragNormal);
    vec3 lightDir = normalize(camera.lightDirection.xyz);
    float diffuse = max(dot(normal, lightDir), 0.0);
    float hemisphere = clamp(normal.y * 0.5 + 0.5, 0.0, 1.0);
    float skyLight = hemisphere * camera.skyLightColor.w;
    float light = camera.fogSettings.w + diffuse * camera.lightDirection.w + skyLight;
    vec3 color = texel.rgb * fragTint * light;

    if (fragBlockType == 7 || fragBlockType == 8)
    {
        color += texel.rgb * 0.25;
    }

    float fogFactor = ComputeFogFactor(fragWorldPosition);
    color = mix(color, camera.fogColor.xyz, fogFactor);

    float alpha = fragBlockType == 5 ? 0.72 * texel.a : texel.a;
    outColor = vec4(color, alpha);
}
