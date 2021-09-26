+-----------------------------+
| R E T R O  3 D  S H A D E R |
+-----------------------------+

Thank you for purchasing Retro 3D Shader! Here is some information to achieve various effects to use
in your projects.

Included are two shaders; one standard for most of your game objects and effects as well as a
secondary liquid shader, great for pools of water, lava, slime, etc.

DIFFUSE - Models texture (preferably point filtered for added effect).

NORMAL - The option is here for normal maps if you choose, though to stay faithful, I strongly 
	 recommend not using normals as it was not common of that era.

COLOR - Used to tint the objects colour.

VERTEX DETAIL - This is used to adjust the vertex snapping effect (jitter) of the model. The 
		default (80) is visually simular to old hardware, but feel free to reduce or
		increase the amount as you see fit!

---

SWIRL AMOUNT* - This controls the tightness of the swirl, higher the value, the more spirals.

INNER SWIRL SPEED* - This controls the intensity of the ping-pong effect for the inner spiral.

OUTER SWIRL SPEED* - This controls the overall rotation of the texture. The outer edges of the texture
		     are not affected by the inner swirl speed, use this value to adjust the rotation
		     of the outer texture.

*Applies only to the liquid shader.

---

BEST PRACTICES

Here are some basic, quick tips to achieve that classic 3D effect.

To gain maximum retro effect, it's recommended to use lower resolution (64px - 128px) textures set at
"Point Filtering". Bilinear filter can also work if you're wanting to target games from a bit later.

There is support for alpha textures, but to keep with the general theme, their rendering has been
set to use alpha clip dithering (which is common for that era) instead of true alpha transparency.

Lighting you want to keep as simple as possible. Avoid using bloom and HDR where possible. Vertex lighting
was far more common, but you can still achieve great effect with the default per pixel. Much earlier 
3D games were simply unlit. As for shadows, hard shadows are recommended.

Be careful when using intense vertex detail amounts. This effect does cause the geometry to actually move
and if used too strongly, can cause open seams large enough for objects to get stuck or fall through.
