// CLIENT PREP: the software renderer starves the bot of frames, so the main camera draws nothing into a tiny texture (the controller still has a camera to aim and cast from).
var cam = Camera.main; if (cam == null) return "no main camera";
cam.cullingMask = 0; cam.targetTexture = new RenderTexture(64, 36, 16);
return "camera tiny";
