// Marmoset Skyshop
// Copyright 2013 Marmoset LLC
// http://marmoset.co

using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Linq;
using mset;

namespace mset {
	public class CubemapGUI {
		public enum Type {
			INPUT,
			SKY,
			SIM,
			DIM
		};
		
		public enum Layout {
			PANO,
			CROSS,
			ROW
		};
		
		public CubeBuffer[]	buffers = null;
		public Texture		input = null;
		public Cubemap 		cube = null;
		public SerializedObject srInput = null;
		public Texture2D 	preview = null;
		private static Texture2D lightGizmo;
		private static Texture2D pickerIcon;
						
		public string		dir = "";
		public string		name = "";
		public string		fullPath = "";
		public string 		inputPath = "";
		public bool			mipmapped;
		public Type			type;
		public bool			inspector = false;		//some features are disabled in inspector mode
		public bool			allowTexture = false;	//allow any kind of texture as input
		
		public bool			showLights = false;
		public bool			assetLinear = false;
		public bool			renderLinear = false;
		public bool			fullPreview = false;
		public int			previewWidth = 320;
		
		public Layout		previewLayout = Layout.ROW;
		private Texture2D	tempTexture = null;
		
		//read-only rectangle 
		private Rect previewRect = new Rect(0,0,0,0);
		
		public ColorMode	colorMode = ColorMode.RGBM8;
		
		//read-only bool for the HDR checkbox
		public bool HDR {
			get { return colorMode == ColorMode.RGBM8; }
			set { colorMode = value ? ColorMode.RGBM8 : ColorMode.RGB8; }
		}
		
		public bool locked = false;
		
		public CubemapGUI(Type _type, bool _inspector) {
			type = _type;
			mipmapped = type == Type.SIM;
			inputPath = "";
			clear();
			inspector = _inspector;
			buffers = new CubeBuffer[4];
			for(int i=0; i<buffers.Length; ++i) {
				buffers[i] = new CubeBuffer();
			}
			
			if( lightGizmo==null ){
				lightGizmo = Resources.Load("light") as Texture2D;
			}
			if( pickerIcon==null ) {
				if( EditorGUIUtility.isProSkin ) {
					pickerIcon = Resources.Load("white_picker") as Texture2D;
				} else {
					pickerIcon = Resources.Load("picker") as Texture2D;
				}
			}
		}
		
		public void freeMem() {
			if(locked) return;
			UnityEngine.Object.DestroyImmediate(preview,false);
			UnityEngine.Object.DestroyImmediate(tempTexture,false);
			preview = null;
			tempTexture = null;
			cube = null;			
			input = null;
			srInput = null;
		}
		
		// REFERENCE
		public void clear() {
			if(locked) return;
			cube = null;
			input = null;
			srInput = null;
			dir = "";
			name = "";
			fullPath = "";
			assetLinear = false;
		}
		
		public bool isLinear() {
			if(srInput == null) return assetLinear;
			srInput.UpdateIfDirtyOrScript();
			return Util.isLinear(srInput);
		}
		
		public void setLinear( bool yes ) {
			if(srInput != null) Util.setLinear(ref srInput, yes);
			//NOTE: assetLinear is updated in updateProperties later, which also knows how to handle preview and buffer refreshes
		}
		
		public void setReference(string path, bool mipped) {
			if(locked) return;
			if( allowTexture ) {
				input = AssetDatabase.LoadAssetAtPath( path, typeof(Texture) ) as Texture;
			} else {
				cube = AssetDatabase.LoadAssetAtPath( path, typeof(Cubemap) ) as Cubemap;
				input = cube;
			}
			if( input == null ) {
				clear();
				return;
			}
			
			if( input.GetType() == typeof(Texture2D) && !Util.isReadableFormat(input as Texture2D) ) {
				Texture2D tex = input as Texture2D;
				Debug.LogError("Unreadable texture compression format: " + tex.format );
				clear();
				return;
			}
						
			fullPath = path;
			updatePath();
			srInput = new SerializedObject(input);
			Util.setReadable(ref srInput,true);
			assetLinear = Util.isLinear(srInput);
			renderLinear = PlayerSettings.colorSpace == ColorSpace.Linear;
			mipmapped = mipped;
			updateBuffers();
			updatePreview();
		}
		
		// FILE PATHS
		private void updatePath() {
			if(locked) return;
			name = Path.GetFileNameWithoutExtension(fullPath);
			dir = Path.GetDirectoryName(fullPath)+"/";
		}		
		private string suggestPath(bool unique) {
			if( inputPath.Length == 0 ) return "";
			
			string postfix = "";
			string fileExt = "";
			switch(type) {
			case Type.SKY:
				postfix = "_SKY";
				fileExt = "cubemap";
				break;
			case Type.DIM:
				postfix = "_DIFF";
				fileExt = "cubemap";
				break;
			case Type.SIM:
				postfix = "_SPEC";
				fileExt = "cubemap";
				break;
			};
			
			int dot = inputPath.LastIndexOf(".");
			string path = inputPath.Substring(0,dot) + postfix + "." + fileExt;
			if( unique ) path = AssetDatabase.GenerateUniqueAssetPath(path);
			return path;
		}
		// find an output cube matching the input texture's path and a list of common, known postfixes
		private string findInput() {
			if(inputPath.Length == 0) return "";
			string searchPath = inputPath;
			int uscore = searchPath.LastIndexOf("_");
			if( uscore > 0 ) searchPath = searchPath.Substring(0,uscore);
			string foundPath = "";
			
			string[] allPaths = AssetDatabase.GetAllAssetPaths();
			string inDir = Path.GetDirectoryName(searchPath).ToLowerInvariant()+"/";
			string inName = Path.GetFileNameWithoutExtension(searchPath).ToLowerInvariant();
			
			switch( type ) {
			case Type.SIM:
				if( findAssetByPostfix( ref foundPath, ref inDir, ref inName, "_spec", ref allPaths) ||
					findAssetByPostfix( ref foundPath, ref inDir, ref inName, "specular", ref allPaths) ||
					findAssetByPostfix( ref foundPath, ref inDir, ref inName, "spec", ref allPaths) ||
					findAssetByPostfix( ref foundPath, ref inDir, ref inName, "sim", ref allPaths) ) {
					return foundPath;
				}
				break;
			case Type.DIM:
				if( findAssetByPostfix( ref foundPath, ref inDir, ref inName, "_diff", ref allPaths) ||
					findAssetByPostfix( ref foundPath, ref inDir, ref inName, "diffuse", ref allPaths) ||
					findAssetByPostfix( ref foundPath, ref inDir, ref inName, "diff", ref allPaths) ||
					findAssetByPostfix( ref foundPath, ref inDir, ref inName, "dim", ref allPaths) ) {
					return foundPath;
				}
				break;
			case Type.SKY:
				if( findAssetByPostfix( ref foundPath, ref inDir, ref inName, "_sky", ref allPaths) ||
					findAssetByPostfix( ref foundPath, ref inDir, ref inName, "skybox", ref allPaths) ||
					findAssetByPostfix( ref foundPath, ref inDir, ref inName, "sky", ref allPaths) ) {
					return foundPath;
				}
				break;
			}
			return "";
		}		
		//Searches a list of asset paths looking for a directory, name, and postfix match. Returns full path of match by reference and true if found.
		private static bool findAssetByPostfix( ref string dstPath, ref string inDir, ref string inName, string postfix) {
			string [] allPaths = AssetDatabase.GetAllAssetPaths();
			return findAssetByPostfix( ref dstPath, ref inDir, ref inName, postfix, ref allPaths );
		}
		private static bool findAssetByPostfix( ref string dstPath, ref string inDir, ref string inName, string postfix, ref string[] allPaths) {
			string dir;
			string name;
			//search by name and dir
			for( int i = 0; i<allPaths.Length; ++i ) {
				if(allPaths[i].Length < 7) continue;
				dir = Path.GetDirectoryName(allPaths[i]).ToLowerInvariant()+"/";
				name = Path.GetFileNameWithoutExtension(allPaths[i]).ToLowerInvariant();
				if( dir == inDir && (inName + postfix) == name ) {
					dstPath = allPaths[i];
					return true;
				}
			}
			//search by name only
			for( int i = 0; i<allPaths.Length; ++i ) {
				if(allPaths[i].Length < 7) continue;
				name = Path.GetFileNameWithoutExtension(allPaths[i]).ToLowerInvariant();
				if( (inName + postfix) == name ) {
					dstPath = allPaths[i];
					return true;
				}
			}
			return false;
		}				
		// draw directional lights from the scene over a panoramic image
		private void drawLightGizmos( Rect rect ) {
			UnityEngine.Object[] lights = GameObject.FindSceneObjectsOfType(typeof(Light));
			for( int i=0; i<lights.Length; ++i ) {
				Light L = (lights[i]) as Light;					
				if( L.type != LightType.Directional ) continue;
				if( L.enabled == false ) continue;
				
				bool selected = Selection.Contains(L.gameObject);
				Vector3 dir = -L.transform.forward;
				if( Sky.activeSky ) {
					//Put everything in relation to the active sky.
					//NOTE: the active sky skybox may not be what's displayed in Skyshop's input preview
					dir = Quaternion.Inverse(Sky.activeSky.transform.rotation) * dir;
				}
				
				float u = 0f;
				float v = 0f;
				if( previewLayout == Layout.PANO ) {
					Util.dirToLatLong(out u, out v, dir);
				} else {
					/*   |+y |
					  ___|___|___ ___
					 |-x |+z |+x |-z |
					 |___|___|___|___|
					     |-y |
					     |___| */

					ulong face = 0;
					Util.cubeLookup(ref u, ref v, ref face, dir);					
					switch((CubemapFace)face) {
					case CubemapFace.NegativeX:
						v+=1f;
						break;
					case CubemapFace.PositiveZ:
						u+=1f;
						v+=1f;
						break;
					case CubemapFace.NegativeY:
						u+=1f;
						v+=2f;
						break;
					case CubemapFace.PositiveY:
						u+=1f;
						break;
					case CubemapFace.PositiveX:
						u+=2f;
						v+=1f;
						break;
					case CubemapFace.NegativeZ:
						u+=3f;
						v+=1f;
						break;
					};
					
					u /= 4f;
					if( previewLayout == Layout.ROW ) v -= 1f;
					else v /= 3f;
				}
				bool clampedLight = v < 0 || v > 1f;
				v = Mathf.Clamp01(v);
				
				Rect imgRect = rect;
				imgRect.x += u*rect.width;
				imgRect.y += v*rect.height;
				
				if( selected ) {
					imgRect.width = imgRect.height = 32;
				} else {
					imgRect.width = imgRect.height = 16;
				}
				if( clampedLight ) {
					imgRect.width *= 0.5f;
					imgRect.height *= 0.5f;
				}
				imgRect.x -= imgRect.width*0.5f;
				imgRect.y -= imgRect.height*0.5f;
				GUI.DrawTexture(imgRect, lightGizmo);
			}
		}
		private void drawButtons( int buttonHeight ){ 			
			EditorGUI.BeginDisabledGroup(locked);
			
			string newTip = "Create a new target cubemap and add it to the project";
			string findTip = "Search project for existing target cubemap by Input Panorama name";
			string clearTip = "Deselect target cubemap";
			//string saveTip = "Save a copy of image to disk as a .hdr file";
			string beastTip = "Export image and set the Beast Lightmapper to use it as global illumination. See \"Beast Global Illum Options\" for more details.";
			
			GUIStyle buttonStyle = new GUIStyle("Button");
			buttonStyle.padding.top = buttonStyle.padding.bottom = 0;
			buttonStyle.padding.left = buttonStyle.padding.right = 0;
				
			EditorGUILayout.BeginHorizontal();{
				EditorGUI.BeginDisabledGroup( inputPath.Length == 0 );{
					if( type != Type.INPUT ) { 					
						if( !inspector ) {
							//NEW
							if( GUILayout.Button(new GUIContent("New",newTip), buttonStyle, GUILayout.Width(55), GUILayout.Height(buttonHeight)) ) {
								fullPath = suggestPath(true);
								updatePath();
								int newSize = 256;
								if(type == Type.SKY) newSize = 512;								
								cube = new Cubemap(newSize, TextureFormat.ARGB32, mipmapped);
								cube.name = name;
								Util.clearCheckerCube(ref cube);
								input = cube;
								AssetDatabase.CreateAsset(cube, fullPath);
								setReference(fullPath, mipmapped);
								Util.setLinear(ref srInput, false);
							}
						}
						//FIND
						if( GUILayout.Button(new GUIContent("Find",findTip), buttonStyle, GUILayout.Width(55), GUILayout.Height(buttonHeight)) ) {
							//look for existing cubemaps with list of known postfixes
							string foundPath = findInput();
							if( foundPath.Length > 0 ) {
								//TODO: technically we should decide if the found cube is mipped or not right here
								setReference(foundPath, mipmapped);
								EditorGUIUtility.PingObject(input);
							}
						}
					}
				}EditorGUI.EndDisabledGroup();
				//NONE
				if( GUILayout.Button(new GUIContent("None",clearTip), buttonStyle, GUILayout.Width(60), GUILayout.Height(buttonHeight)) ) {
					clear();
				}
				EditorGUI.BeginDisabledGroup(this.input == null);{
					/*if( GUILayout.Button(new GUIContent("Save",saveTip), buttonStyle, GUILayout.Width(60), GUILayout.Height(buttonHeight)) ) {
						string name = Path.GetFileNameWithoutExtension(fullPath);
						string path = EditorUtility.SaveFilePanel("Save HDR image", "", name+".hdr", "hdr");
						if(!string.IsNullOrEmpty(path)) {
							updateBuffers();
							int width = this.buffers[0].width*4;
							int height = this.buffers[0].width*2;
							Color[] pixels = new Color[width*height];
							this.buffers[0].toPanoBuffer(ref pixels, width, height);
							HDRProcessor.writeHDR(ref pixels, width, height, path);
						}
					}
					*/
					
					Rect giRect = GUILayoutUtility.GetLastRect();
					if( GUILayout.Button(new GUIContent("Use as GI",beastTip), buttonStyle, GUILayout.Width(75), GUILayout.Height(buttonHeight)) ) {
						if( type == Type.SIM && this.mipmapped ) {
							GenericMenu genericMenu = new GenericMenu();
							string[] names = new string[] {"Mip 0","Mip 1", "Mip 2", "Mip 3"};
							genericMenu.AddItem(new GUIContent(names[0]), true,  exportBufferCallback, 0);
							genericMenu.AddItem(new GUIContent(names[1]), false, exportBufferCallback, 1);
							genericMenu.AddItem(new GUIContent(names[2]), false, exportBufferCallback, 2);
							genericMenu.AddItem(new GUIContent(names[3]), false, exportBufferCallback, 3);
							genericMenu.DropDown(giRect);
						} else {
							exportBufferCallback(0);
						}
						
					}
				}EditorGUI.EndDisabledGroup();
			}EditorGUILayout.EndHorizontal();
			EditorGUI.EndDisabledGroup();
		}
		
		private void exportBufferCallback(object data) {
			SerializedObject srConfig = BeastConfig.getSerializedConfig();
			string name = "BeastSky_noimport";
			string dir = BeastConfig.configFilePath;
			
			if(!string.IsNullOrEmpty(dir)) {
				updateBuffers();
				int mip = (int)data;
				if( buffers[mip].empty() ) mip = 0;
				if( buffers[mip].empty() ) return;
				string path = Path.GetFullPath( Path.GetDirectoryName(dir) + "/" + name + ".hdr" );
				path = path.Replace('\\','/'); //stop it, windows.				
				int width = this.buffers[mip].width*4;
				int height = this.buffers[mip].width*2;
				Color[] pixels = new Color[width*height];
				this.buffers[mip].toPanoBuffer(ref pixels, width, height);
				HDRProcessor.writeHDR(ref pixels, width, height, path);
											
				SerializedProperty iblImageFile =  srConfig.FindProperty ("config.environmentSettings.iblImageFile");
				SerializedProperty giEnvironment = srConfig.FindProperty ("config.environmentSettings.giEnvironment");
				iblImageFile.stringValue = path;
				giEnvironment.intValue = (int)ILConfig.EnvironmentSettings.Environment.IBL;
				srConfig.ApplyModifiedProperties();
				
				BeastConfig.SaveConfig();
			}
		}
		
		public void update() {
			bool dirtyPreview = false;
			bool dirtyBuffers = false;
			updateProperties(ref dirtyPreview, ref dirtyBuffers);
			if( dirtyBuffers ) updateBuffers();
			if( dirtyPreview ) updatePreview();
		}
		
		private void updateProperties(ref bool dirtyPreview, ref bool dirtyBuffers) {
			if( locked ) return;
			//If Unity is configured to render in linear space, the gui preview texture data is expected to be in sRGB space
			bool prevLinear = renderLinear;
			renderLinear = PlayerSettings.colorSpace == ColorSpace.Linear;
			if( prevLinear != renderLinear ) {
				dirtyPreview = true;
			}
			if(input == null) srInput = null;
			if(srInput != null) {
				srInput.UpdateIfDirtyOrScript();
				//If the cubemap asset is changed to be "Linear" aka "Bypass sRGB Sampling", we need to redo our preview and reimport the buffer
				prevLinear = assetLinear;
				assetLinear = Util.isLinear(srInput);
				if( prevLinear != assetLinear ) {
					dirtyPreview = true;
					dirtyBuffers = true;
				}
			}
		}
		
		// GUI
		public static CubeBuffer.FilterMode sFilterMode = CubeBuffer.FilterMode.BILINEAR;
		public static void drawStaticGUI() {
			//TODO: until bicubic is figured out properly, this is pulled.
			/*sFilterMode = (CubeBuffer.FilterMode)EditorGUILayout.EnumPopup(
				new GUIContent("Filtering Mode","Resampling method for upscaling or downscaling image data."),
				sFilterMode,
				GUILayout.Width(300)
			);*/
		}
		private void applyStaticGUI() {
			if( locked ) return;
			for(int i=0; i<buffers.Length; ++i) {
				buffers[i].filterMode = sFilterMode;
			}
		}
		public void drawGUI() {
			EditorGUI.BeginDisabledGroup(locked);
			
			applyStaticGUI();
			string label = "";
			string previewLabel = "";
			string toolTip = "";
			
			bool dirtyPreview = false;
			bool dirtyBuffers = false;
			
			updateProperties(ref dirtyPreview, ref dirtyBuffers);
			
			switch(type) {
			case Type.INPUT:
				label = "INPUT PANORAMA";
				toolTip = "Input Panorama - \nCubemap, panorama, or horizontal-cross image of the sky that will light the scene.";
				previewLabel = "Input Panorama";
				break;
			case Type.SKY:
				label = "SKYBOX";
				toolTip = "Skybox - \nTarget cubemap to be used as the scene background.";
				previewLabel = "Skybox Preview";
				break;
			case Type.DIM:
				label = "DIFFUSE OUTPUT";
				toolTip = "Diffuse Output -\nTarget cubemap for storing diffuse lighting data.";
				previewLabel = "Diffuse Preview";
				break;
			case Type.SIM:
				label = "SPECULAR OUTPUT";
				toolTip = "Specular Output -\nTarget cubemap for storing specular lighting data.";
				previewLabel = "Specular Preview";
				break;
			};
			
			EditorGUILayout.BeginHorizontal();{
				Texture prev = input;
				if( allowTexture ) {
					input = (Texture)EditorGUILayout.ObjectField(input, typeof(Texture), false, GUILayout.Width(57), GUILayout.Height(57));
				} else {
					cube = (Cubemap)EditorGUILayout.ObjectField(cube, typeof(Cubemap), false, GUILayout.Width(57), GUILayout.Height(57));
					input = cube;
				}
				// new cubemap target
				if(input != prev) {
					fullPath = AssetDatabase.GetAssetPath(input);
					setReference(fullPath,mipmapped);
				}
				
				EditorGUILayout.BeginVertical(); {
					EditorGUILayout.LabelField(new GUIContent(label, toolTip), GUILayout.Height(14));
					EditorGUI.BeginDisabledGroup(true);
						EditorGUILayout.LabelField(dir + name, GUILayout.Height(14));
					EditorGUI.EndDisabledGroup();					
					EditorGUILayout.Space();
					drawButtons(15);					
				}EditorGUILayout.EndVertical();	
			}EditorGUILayout.EndHorizontal();
			
			float offset = 0f;
			if( inspector ) offset = 4f;
			if( previewLayout == Layout.CROSS ) {
				if( input )	previewRect = Util.GUILayout.drawTexture(offset, 0, (float)previewWidth, 3f*previewWidth/4f, previewLabel, preview, false);
				else 		previewRect = Util.GUILayout.drawTexture(offset, 0, (float)previewWidth, 3f*previewWidth/4f, previewLabel, null, false);
			} else if( previewLayout == Layout.PANO ) {
				if( input )	previewRect = Util.GUILayout.drawTexture(offset, 0, (float)previewWidth, previewWidth/2f, previewLabel, preview, false);
				else 		previewRect = Util.GUILayout.drawTexture(offset, 0, (float)previewWidth, previewWidth/2f, previewLabel, null, false);
			} else {
				if( input )	previewRect = Util.GUILayout.drawTexture(offset, 0, (float)previewWidth, previewWidth/4f, previewLabel, preview, false);
				else 		previewRect = Util.GUILayout.drawTexture(offset, 0, (float)previewWidth, previewWidth/4f, previewLabel, null, false);
			}
			
			//draw light overlay
			if(showLights) drawLightGizmos(previewRect);
			
			int buttonHeight = 16;
			Rect previewBar = GUILayoutUtility.GetRect(320, buttonHeight);
			previewBar.x += offset+2;
			previewBar.y -= buttonHeight+2;
			previewBar.width = 60;
			previewBar.height = 16;
			GUI.Box(previewBar,"","HelpBox");
			previewBar.width = previewWidth;
			
			previewBar.y -= 1;
			Rect rect = previewBar;
			rect.x += 2;
			rect.width = 42;
			rect.height = 16;
			
			//if HDR was toggled, we need to reimport the buffers and recompute our preview
			string HDRTip;
			if( inspector ) {
				HDRTip = "Treat cubemap as RGBM-encoded HDR data.";
			} else {
				if( type == Type.INPUT ) HDRTip = "Treat input as RGBM-encoded HDR image.";
				else 					 HDRTip = "Write output as RGBM-encoded HDR image.";
			}
			
			bool newHDR = GUI.Toggle(rect, HDR, new GUIContent("HDR", HDRTip));
			if( newHDR != HDR ) {
				HDR = newHDR;
				dirtyBuffers = true;
				dirtyPreview = true;
			}
		
			/*
			//This is no longer editable. Skyshop sets it to what skyshop needs. 
			rect.x += 60;
			rect.width = 60;
			bool prevLinear = assetLinear;
			assetLinear = GUI.Toggle(rect, assetLinear, "Linear");
			if( prevLinear != assetLinear ) {
				setLinear(assetLinear);
				dirtyBuffers = true;
				dirtyPreview = true;
			}*/
		
			rect = previewBar;
			rect.x += previewBar.width - 15;
			rect.width = 20;
			
			bool pick = false;
			if( showLights ) pick = Util.GUILayout.tinyButton(rect.x-53, rect.y+1, pickerIcon, "Pick light color from image", -1,1);
			showLights = Util.GUILayout.tinyToggle(rect.x-40, rect.y+1, "*", "Show/Hide directional lights", 1, 3, showLights);
			bool R = Util.GUILayout.tinyButton(rect.x-24, rect.y+1, "R", "Row preview", 1, 1);
			bool P = Util.GUILayout.tinyButton(rect.x-12, rect.y+1, "P", "Panorama preview", 1, 1);
			bool C = Util.GUILayout.tinyButton(rect.x-0,  rect.y+1, "C", "Cross preview", 0, 1);
			
			if( R ) {
				previewLayout = Layout.ROW;
				dirtyPreview = true;
			}
			if( P ) {
				previewLayout = Layout.PANO;
				dirtyPreview = true;
			}
			if( C ) {
				previewLayout = Layout.CROSS;
				dirtyPreview = true;
			}
			
			if( pick ) {
				Light[] lights = new Light[Selection.transforms.Length];
				int lightCount = 0;
				for( int i=0; i<Selection.transforms.Length; ++i ) {
					Light L = Selection.transforms[i].gameObject.GetComponent<Light>();
					if( L == null || L.type != LightType.Directional ) continue;					
					lights[lightCount] = L;
					lightCount++;
				}
				Undo.RegisterUndo(lights, "Light Color Picking");
				for( int i=0; i<lightCount; ++i ) {
					Light L = lights[i];
					float u=0f;
					float v=0f;
					ulong face = 0;
					Util.cubeLookup(ref u, ref v, ref face, -L.transform.forward);
					Color pixel = Color.black;
					//TODO: multisampling
					buffers[0].sampleNearest(ref pixel, u, v, (int)face);
					float intens = Mathf.Max(pixel.r,Mathf.Max(pixel.g,pixel.b));
					if( intens > 1f ) {
						pixel /= intens;
						L.intensity = intens;
					} else {
						L.intensity = 1f;
					}
					//Q: should light color be gamma or linear? A: linear, so pull it straight from buffer[0]
					//if( !assetLinear ) Util.applyGamma(ref pixel, Gamma.toSRGB);
					L.color = pixel;
				}
			}
			
			if( dirtyBuffers ) updateBuffers();
			if( dirtyPreview ) updatePreview();
			EditorGUI.EndDisabledGroup();
		}
				
		// PREVIEW
		public void updateBuffers() {
			if(locked || !input) return;
			srInput.Update();
			bool splitMips = mipmapped && input.width>8 && input.height>8 && Util.isMipmapped(srInput);
			
			//HACK if the input is a sky, limit its size to a height of 256, just to keep the preview image under control
			int mip = 0;
			if( type==Type.SKY && input.height > 256 && splitMips ) {
				mip = QPow.Log2i(cube.width) - 8; //find the mip for 256x256
			}
			
			bool useGamma = !assetLinear;
			if( input.GetType() == typeof(Cubemap) ) {
				cube = (Cubemap)input;
				buffers[0].fromCube(cube, mip, colorMode, useGamma);
				if(splitMips) {
					buffers[1].fromCube(cube, 1, colorMode, useGamma);
					buffers[2].fromCube(cube, 2, colorMode, useGamma);
					buffers[3].fromCube(cube, 3, colorMode, useGamma);
				}
			}
			else if( input.GetType() == typeof(Texture2D) ) {
				string ipath = AssetDatabase.GetAssetPath(input);
				TextureImporter ti = Util.getTextureImporter(ipath, "CubemapGUI");
				if( ti ) {
					ti.npotScale = TextureImporterNPOTScale.None;
					ti.isReadable = true;
					AssetDatabase.ImportAsset(ipath);
				}
				//horizontal cross
				if( 4*input.height == 3*input.width ) {
					buffers[0].fromHorizCrossTexture((Texture2D)input, 0, colorMode, useGamma);
					if(splitMips) {
						//TODO: no mips on non PoT textures (ie 4:3 anything)
						buffers[1].clear();
						buffers[2].clear();
						buffers[3].clear();
					}
				}
				//vertical column
				else if( 1*input.height == 6*input.width ) {
					buffers[0].fromColTexture((Texture2D)input, mip, colorMode, useGamma);
					if(splitMips) {
						//no mips on non PoT textures (ie 6:1 anything)
						buffers[1].clear();
						buffers[2].clear();
						buffers[3].clear();
					}
				}
				//panorama
				else {
					int faceSize = Mathf.NextPowerOfTwo(input.height); // how big we want to make our cubemap
					if( type == Type.INPUT ) faceSize = Mathf.Min(faceSize,1024);	//allow for bigger input buffer, this is what gets used for computation
					else 					 faceSize = Mathf.Min(faceSize,256);	//other types only use the buffers for previewing, 256 per face is fine
					buffers[0].fromPanoTexture((Texture2D)input, faceSize, colorMode, useGamma);
					if(splitMips) {
						//TODO: no mip support in fromPano right now!
						buffers[1].clear();
						buffers[2].clear();
						buffers[3].clear();
					}
				}
			} else {
				Debug.LogError("Invalid texture type for CubeGUI input!");
			}
		}
		
		public void updatePreview() {
			if( locked || !input ) return;
			
			int previewFaceSize = previewWidth/4;
			int previewHeight = previewFaceSize;
			
			switch( previewLayout ) {
			case Layout.ROW:
				previewHeight = previewFaceSize;
				break;
			case Layout.CROSS:
				previewHeight = 3*previewFaceSize;
				break;
			case Layout.PANO:
				previewHeight = 2*previewFaceSize;
				break;
			};
			
			if( !preview ) {
				preview = new Texture2D(previewWidth, previewHeight,TextureFormat.ARGB32, false, true);
				preview.hideFlags |= HideFlags.DontSave; //do not save this and do not clear it coming back from play mode
			}
			else if( preview.width != previewWidth || preview.height != previewHeight ) {
				preview.Resize(previewWidth, previewHeight);
			}
			if( previewLayout == Layout.CROSS ) {
				Util.clearTo2D(ref preview, Color.black);
			}
			
			int faceSize = buffers[0].faceSize;
			
			//many input formats don't have mipmapped buffers, sample high mip
			bool splitMips = mipmapped &&
							faceSize>8 &&
							!buffers[1].empty() &&
							!buffers[2].empty() &&
							!buffers[3].empty();
			
			
			float gamma = 1f;
			if(!assetLinear) {//renderLinear) {
				//if linear-space rendering, we're expecting a gamma curve to be applied to the image before display
				gamma *= Gamma.toSRGB;
			}
	
			if( previewLayout == Layout.PANO ) {
				ulong w = (ulong)previewWidth;
				ulong h = (ulong)previewHeight;
				
				Color pixel = new Color();
				for(ulong x=0; x<w; ++x)
				for(ulong y=0; y<h; ++y) {
					float u=0f;
					float v=0f;
					ulong face=0;
					Util.latLongToCubeLookup(ref u, ref v, ref face, x, y, w, h);
					int mip=0;
					if( splitMips ) mip = (int)(x*4/w);
					buffers[mip].sample(ref pixel, u, v, (int)face);
					pixel.r = Mathf.Clamp01(pixel.r);
					pixel.g = Mathf.Clamp01(pixel.g);
					pixel.b = Mathf.Clamp01(pixel.b);
					if( gamma != 1f ) {  Util.applyGamma(ref pixel, gamma); }
					preview.SetPixel((int)x,(int)y,pixel);
				}
				preview.Apply();
			} else {
				Color[] facePixels = new Color[previewFaceSize * previewFaceSize];
				int faceCount;
				if( previewLayout == Layout.CROSS )	faceCount = 6;
				else  								faceCount = 4;
				for( int face = 0; face<faceCount; ++face ) {
					int mip = 0;
					if( splitMips ) {
						mip = face;
						if( face >= 4 ) mip = 1;
					}
					int sampleFace = face;
					switch(face) {
						case 0: sampleFace = (int)CubemapFace.NegativeX; break;
						case 1: sampleFace = (int)CubemapFace.PositiveZ; break;
						case 2: sampleFace = (int)CubemapFace.PositiveX; break;
						case 3: sampleFace = (int)CubemapFace.NegativeZ; break;
						case 4: sampleFace = (int)CubemapFace.PositiveY; break;
						case 5: sampleFace = (int)CubemapFace.NegativeY; break;
					};
					if( buffers[mip].pixels == null ) {
						Debug.LogError("null pixels on mip " + mip);
					}
					buffers[mip].resampleFace(ref facePixels, sampleFace, previewFaceSize, true);
					CubeBuffer.encode(ref facePixels, facePixels, ColorMode.RGB8, false);
					if( gamma != 1f ) { Util.applyGamma(ref facePixels, gamma); }
					
					if( previewLayout == Layout.CROSS ) {
						if( face < 4 )			preview.SetPixels( previewFaceSize*face, 1*previewFaceSize, previewFaceSize, previewFaceSize, facePixels );
						else if( face == 4 )	preview.SetPixels( previewFaceSize*1,    2*previewFaceSize, previewFaceSize, previewFaceSize, facePixels );
						else if( face == 5 )	preview.SetPixels( previewFaceSize*1,    0,					previewFaceSize, previewFaceSize, facePixels );
					} else {
						preview.SetPixels( previewFaceSize*face, 0, previewFaceSize, previewFaceSize, facePixels );
					}
				}
				preview.Apply();
			}
		}
	};
}