#version 450

layout(location = 0) in vec3 fragNormal;
layout(location = 1) in vec2 fragUv;
layout(location = 2) flat in uint fragBlockType;
layout(location = 3) in vec3 fragTint;
layout(location = 4) in vec3 fragWorldPosition;
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
} camera;

layout(set = 0, binding = 1) uniform sampler2D blockAtlas;
layout(set = 0, binding = 2) uniform sampler2D environmentAtlas;

vec4 SampleAnimatedCloud(vec2 atlasUv, vec3 worldPosition, float timeSeconds)
{
    vec2 atlasSize = vec2(textureSize(environmentAtlas, 0));
    vec2 inset = 0.5 / atlasSize;
    vec2 tileMin = vec2(0.0, 0.0);
    vec2 tileSpan = vec2(1.0, 0.5);
    vec2 localUv = (atlasUv - (tileMin + inset)) / (tileSpan - inset * 2.0);

    vec2 drift = vec2(timeSeconds * 0.0065, timeSeconds * 0.0018);
    vec2 primaryUv = fract(localUv + drift);
    vec2 detailUv = fract(localUv * 1.85 + drift * 1.65 + vec2(worldPosition.x, worldPosition.z) * 0.0007);

    vec2 primaryAtlasUv = tileMin + inset + primaryUv * (tileSpan - inset * 2.0);
    vec2 detailAtlasUv = tileMin + inset + detailUv * (tileSpan - inset * 2.0);

    vec4 primary = texture(environmentAtlas, primaryAtlasUv);
    vec4 detail = texture(environmentAtlas, detailAtlasUv);
    float density = clamp(primary.a * 0.72 + detail.a * 0.58, 0.0, 1.0);
    vec3 color = mix(primary.rgb, detail.rgb, 0.35);
    return vec4(color, density);
}

void main()
{
    bool isCloud = fragBlockType == 200;
    bool isSun = fragBlockType == 201;
    vec4 texel;
    if (isCloud || isSun)
    {
        texel = isCloud
            ? SampleAnimatedCloud(fragUv, fragWorldPosition, camera.cameraPosition.w)
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

    if (isCloud)
    {
        vec3 normal = normalize(fragNormal);
        float topLight = clamp(normal.y * 0.5 + 0.5, 0.0, 1.0);
        float rim = pow(1.0 - clamp(dot(normalize(camera.cameraPosition.xyz - fragWorldPosition), normal), 0.0, 1.0), 1.5);
        float shading = mix(0.78, 1.08, topLight) + rim * 0.08;
        vec3 cloudColor = texel.rgb * fragTint * shading;
        outColor = vec4(cloudColor, texel.a * 0.58);
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

    float distanceToCamera = distance(fragWorldPosition, camera.cameraPosition.xyz);
    float fogFactor = smoothstep(camera.fogSettings.x, camera.fogSettings.y, distanceToCamera);
    fogFactor *= exp2(-max(fragWorldPosition.y - camera.cameraPosition.y, 0.0) * camera.fogSettings.z);
    fogFactor = clamp(fogFactor, 0.0, 1.0);
    color = mix(color, camera.fogColor.xyz, fogFactor);

    float alpha = fragBlockType == 5 ? 0.72 * texel.a : texel.a;
    outColor = vec4(color, alpha);
}
