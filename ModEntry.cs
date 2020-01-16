using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Audio;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Buildings;
using Harmony;
using Netcode;
using System.Reflection;
using Newtonsoft.Json;

namespace LavapulseChickens
{
    public class ModEntry : Mod, IAssetEditor, IAssetLoader
    {
        static Dictionary<int, string> eggsAndHatchedAnimals = new Dictionary<int, string>
        {
            { 107, "Dinosaur" },
            { 174, "White Chicken" },
            { 176, "White Chicken" },
            { 180, "Brown Chicken" },
            { 182, "Brown Chicken" },
            { 305, "Void Chicken" },
            { 442, "Duck" }
        };
        public static Dictionary<NetLong, string> modifiedAnimalSounds = new Dictionary<NetLong, string>();
        public static List<int> unloadedEggIDs = new List<int>();
        public Dictionary<int, string> customEggTextures = new Dictionary<int, string>();
        public Dictionary<string, string> eggSourcePack = new Dictionary<string, string>();
        public Dictionary<string, string> animalSourcePack = new Dictionary<string, string>();
        public Dictionary<string, List<string>> customChickenTextures = new Dictionary<string, List<string>>();
        public Dictionary<string, IContentPack> contentPackNames = new Dictionary<string, IContentPack>();
        public Dictionary<int, Vector2> eggMultipliers = new Dictionary<int, Vector2>();
        public Dictionary<string, List<SoundEffectInstance>> soundEffects = new Dictionary<string, List<SoundEffectInstance>>();
        static List<int> smallChickenEggIDs = new List<int> {
            176, 180
        };
        static IModHelper smapiHelper;
        static ModEntry modEntry;
        static string dataFolderName = "LavapulseModData";
        static string dataFileName = "LavapulseChickens.json";
        static string dataFilePath = "";
        static string contentFileName = "content.json";
        public string eventText = "";

        List<Item> eggsOfTheDay = new List<Item>();
        Random rand = new Random();
        ModConfig config;
        ModData modData;
        Dictionary<string, int> jaData = new Dictionary<string, int>();
        bool chickenContentHandled = false;
        bool eggContentHandled = false;
        int largestItemID = 816;
        int dailyEggsLastUpdated = 0;

        public static int modDataColorsIndex = 0;
        public static int modDataBreedIndex = 1;
        public static int modDataEggAspectIndex = 2;
        public static int modDataEggNameIndex = 3;
        public static int modDataPriceIndex = 4;
        public static int modDataSoundIndex = 5;
        public static int modDataObtainIndex = 6;
        public static int modDataObtainMethodIndex = 7;
        public static int modDataPackIndex = 8;
        StardewValley.Menus.IClickableMenu currentMenu = null;
        public override void Entry(IModHelper helper)
        {
            smapiHelper = helper;
            modEntry = this;
            config = helper.ReadConfig<ModConfig>();

            SMAPIEventHooks();
            HarmonyPatches();
        }
        private void ReplaceCustomAnimalSounds()
        {
            Farm farm = Game1.getFarm();
            if (farm != null)
            {
                foreach (FarmAnimal animal in farm.getAllFarmAnimals())
                {
                    ReplaceCustomAnimalSound(animal);
                }
            }
        }
        private void ReplaceCustomAnimalSound(FarmAnimal animal)
        {
            if (animal != null && animal.sound != null)
            {
                string animalSound = animal.sound.Value;
                if (soundEffects.ContainsKey(animalSound))
                {
                    NetLong animalID = animal.myID;
                    animal.sound.Value = null;
                    if (animalID != null && modifiedAnimalSounds != null && !modifiedAnimalSounds.ContainsKey(animalID))
                    {
                        modifiedAnimalSounds.Add(animalID, animalSound);
                    }
                }
            }
        }
        private void LoadContentPacks()
        {
            List<string> packsNotLoaded = new List<string>(modData.packsLoaded.Keys);

            ContentPackModel contentPackData = smapiHelper.Data.ReadJsonFile<ContentPackModel>(contentFileName);
            ProcessContentPackData(contentPackData, null);
            packsNotLoaded.Remove("Chickens Pack");

            foreach (IContentPack contentPack in smapiHelper.ContentPacks.GetOwned())
            {
                contentPackData = contentPack.ReadJsonFile<ContentPackModel>(contentFileName);
                ProcessContentPackData(contentPackData, contentPack);
                packsNotLoaded.Remove(contentPackData.packName);
            }

            foreach(string packNotLoaded in packsNotLoaded)
            {
                UnloadContentPackData(packNotLoaded);
            }
            unloadedEggIDs = GetPackDataReservedIDs("");
            SaveModData();
        }
        private void ProcessContentPackData(ContentPackModel contentPackData, IContentPack contentPack)
        {
            if (contentPackData != null && contentPackData.packName != "")
            {
                ImportSounds(contentPackData, contentPack);
                if (contentPack != null && !contentPackNames.ContainsKey(contentPackData.packName))
                {
                    contentPackNames.Add(contentPackData.packName, contentPack);
                }
                if (!modData.packsLoaded.ContainsKey(contentPackData.packName))
                {
                    LoadContentPackData(contentPackData);
                }
                else if (contentPackData.packVersion != modData.packsLoaded[contentPackData.packName])
                {
                    UnloadContentPackData(contentPackData.packName);
                    LoadContentPackData(contentPackData);
                }
            }
        }
        private void ImportSounds(ContentPackModel contentPackData, IContentPack contentPack)
        {
            string soundsFolderPath = System.IO.Path.Combine("assets", "sounds");
            foreach(string soundName in contentPackData.soundEffects)
            {
                List<SoundEffectInstance> sfx = new List<SoundEffectInstance>();
                string soundDirectory;
                if(contentPack != null)
                {
                    soundDirectory = System.IO.Path.Combine(contentPack.DirectoryPath, soundsFolderPath);
                } else
                {
                    soundDirectory = System.IO.Path.Combine(smapiHelper.DirectoryPath, soundsFolderPath);
                }
                if (System.IO.Directory.Exists(soundDirectory))
                {
                    foreach(string soundPath in System.IO.Directory.GetFiles(soundDirectory, soundName + "*.wav"))
                    {
                        sfx.Add(MakeSoundEffect(soundPath));
                    }
                }
                soundEffects.Add(soundName, sfx);
            }
        }
        private SoundEffectInstance MakeSoundEffect(string soundPath)
        {
            SoundEffect sfx = null;
            using (System.IO.Stream fileStream = System.IO.File.OpenRead(soundPath))
            {
                sfx = SoundEffect.FromStream(fileStream);
            }
            return sfx.CreateInstance();
        }
        private void UnloadContentPackData(string packName)
        {
            List<int> IDsofLastUnloadedContent = new List<int>();
            List<int> customEaHAKeys = new List<int>(modData.customEggsAndHatchedAnimals.Keys);
            customEaHAKeys.Sort();
            foreach(int key in customEaHAKeys)
            {
                if (modData.customEggsAndHatchedAnimals[key].Contains(packName))
                {
                    IDsofLastUnloadedContent.Add(key);
                    modData.customEggsAndHatchedAnimals.Remove(key);
                }
            }
            modData.packsLoaded.Remove(packName);
            modData.packsUnloaded.Add(packName, IDsofLastUnloadedContent);
        }
        private void LoadContentPackData(ContentPackModel contentPackData)
        {
            List<int> reservedIDs = GetPackDataReservedIDs(contentPackData.packName);
            int nextIndex = GetNextDataIndex(0, contentPackData.packName, reservedIDs);
            foreach (string[] animalAndEggData in contentPackData.animalAndEggData)
            {
                if (!(animalAndEggData[2] == "" || animalAndEggData[3] == ""))
                {
                    modData.customEggsAndHatchedAnimals.Add(nextIndex, new List<string>() { animalAndEggData[0], animalAndEggData[1], animalAndEggData[2], animalAndEggData[3], animalAndEggData[4], animalAndEggData[8], animalAndEggData[9], animalAndEggData[10], contentPackData.packName });
                    nextIndex = GetNextDataIndex(nextIndex, contentPackData.packName, reservedIDs);
                }
                if (!(animalAndEggData[4] == "" || animalAndEggData[5] == ""))
                {
                    modData.customEggsAndHatchedAnimals.Add(nextIndex, new List<string>() { animalAndEggData[0], animalAndEggData[1], animalAndEggData[5], animalAndEggData[6], animalAndEggData[7], animalAndEggData[8], animalAndEggData[9], animalAndEggData[10], contentPackData.packName });
                    nextIndex = GetNextDataIndex(nextIndex, contentPackData.packName, reservedIDs);
                }
            }
            modData.packsLoaded.Add(contentPackData.packName, contentPackData.packVersion);
            if (modData.packsUnloaded.ContainsKey(contentPackData.packName))
            {
                modData.packsUnloaded.Remove(contentPackData.packName);
            }
        }
        private List<int> GetPackDataReservedIDs(string packName)
        {
            List<int> reservedIDs = new List<int>();
            foreach (string unloadedPack in modData.packsUnloaded.Keys)
            {
                if (unloadedPack != packName)
                {
                    foreach (int ID in modData.packsUnloaded[unloadedPack])
                    {
                        reservedIDs.Add(ID);
                    }
                }
            }
            reservedIDs.Sort();
            return reservedIDs;
        }
        private int GetNextDataIndex(int currentIndex, string packName, List<int> reservedIDs)
        {
            int newIndex = currentIndex;
            if (modData.packsUnloaded.ContainsKey(packName) && modData.packsUnloaded[packName].Count > 0)
            {
                newIndex = modData.packsUnloaded[packName][0];
                modData.packsUnloaded[packName].RemoveAt(0);
            }
            else
            {
                if (currentIndex == 0)
                {
                    List<int> customEggIndexes = new List<int>(modData.customEggsAndHatchedAnimals.Keys);
                    if (customEggIndexes.Count > 0)
                    {
                        customEggIndexes.Sort();
                        newIndex = customEggIndexes[customEggIndexes.Count - 1] + 1;
                    }
                }
                else
                {
                    newIndex++;
                }
            }
            if (reservedIDs.Contains(newIndex))
            {
                newIndex = reservedIDs[reservedIDs.Count - 1] + 1;
            }
            return newIndex;
        }
        private void SetUpChickenData()
        {
            if (!chickenContentHandled)
            {
                List<string> sourceImagePath;
                foreach (List<string> hatchedAnimalsData in modData.customEggsAndHatchedAnimals.Values)
                {
                    string[] animalColors = hatchedAnimalsData[modDataColorsIndex].Split(new string[] { ", " }, StringSplitOptions.None);
                    string breed = hatchedAnimalsData[modDataBreedIndex];
                    foreach (string color in animalColors)
                    {
                        if (!customChickenTextures.ContainsKey(color + breed))
                        {
                            sourceImagePath = new List<string>() {
                                System.IO.Path.Combine("assets", "animals", color + breed + ".png"),
                                System.IO.Path.Combine("assets", "animals", "Baby" + color + breed + ".png"),
                                System.IO.Path.Combine("Animals", color + breed),
                                System.IO.Path.Combine("Animals", "Baby" + color + breed)
                            };
                            customChickenTextures.Add(color + breed, sourceImagePath);
                            if(hatchedAnimalsData[modDataPackIndex] != "Chickens Pack" && !animalSourcePack.ContainsKey(color + breed))
                            {
                                animalSourcePack.Add(color + breed, hatchedAnimalsData[modDataPackIndex]); 
                            }
                        }
                    }
                }
                chickenContentHandled = true;
            }
        }
        private void SetUpEggData(IModHelper helper)
        {
            if (!eggContentHandled)
            {
                foreach (int id in modData.customEggsAndHatchedAnimals.Keys)
                {
                    customEggTextures.Add(id, System.IO.Path.Combine("LooseSprites", modData.customEggsAndHatchedAnimals[id][modDataEggNameIndex]));
                    if(modData.customEggsAndHatchedAnimals[id][modDataPackIndex] != "Chickens Pack" && !eggSourcePack.ContainsKey(modData.customEggsAndHatchedAnimals[id][modDataEggNameIndex]))
                    {
                        eggSourcePack.Add(modData.customEggsAndHatchedAnimals[id][modDataEggNameIndex], modData.customEggsAndHatchedAnimals[id][modDataPackIndex]);
                    }
                }
                eggContentHandled = true;
            }
        }

        public bool CanEdit<T>(IAssetInfo asset)
        {
            if (asset.AssetNameEquals(System.IO.Path.Combine("Data", "ObjectInformation")) || asset.AssetNameEquals(System.IO.Path.Combine("Data", "FarmAnimals")) || asset.AssetNameEquals(System.IO.Path.Combine("Maps", "springobjects")))
            {
                return true;
            }

            return false;
        }

        public bool CanLoad<T>(IAssetInfo asset)
        {
            foreach(List<string> texturePaths in customChickenTextures.Values)
            {
                if (texturePaths.Contains(asset.AssetName))
                {
                    return true;
                }
            }
            if (customEggTextures.ContainsValue(asset.AssetName))
            {
                return true;
            }
            return false; 
        }
        private void FindIDsForEggs()
        {
            int lastIDFound = -1;
            Dictionary<int, List<string>> customEggsAndHatchedAnimalsCopy = new Dictionary<int, List<string>>(modData.customEggsAndHatchedAnimals);
            foreach (int id in customEggsAndHatchedAnimalsCopy.Keys)
            {

                int thisItemID = id;
                if (!IsValidItemID(thisItemID))
                {
                    if (lastIDFound > 0)
                    {
                        thisItemID = lastIDFound + 1;
                    }
                    else
                    {
                        thisItemID = largestItemID + 1;
                    }
                    if (jaData.Count > 0 && jaData.ContainsValue(thisItemID))
                    {
                        List<int> jaDataIDs = new List<int>(jaData.Values);
                        jaDataIDs.Sort();
                        thisItemID = jaDataIDs[jaDataIDs.Count - 1] + 1;
                    }
                    lastIDFound = thisItemID;
                }

                if (!eggsAndHatchedAnimals.ContainsKey(thisItemID))
                {
                    eggsAndHatchedAnimals.Add(thisItemID, modData.customEggsAndHatchedAnimals[id][modDataColorsIndex].Replace(",", modData.customEggsAndHatchedAnimals[id][modDataBreedIndex] + ",") + modData.customEggsAndHatchedAnimals[id][modDataBreedIndex]);
                    if (!smallChickenEggIDs.Contains(thisItemID) && modData.customEggsAndHatchedAnimals[id][modDataObtainIndex] == "Marnie" && modData.customEggsAndHatchedAnimals[id][modDataObtainMethodIndex] == "Egg" && !IsDeluxeEgg(id))
                    {
                        smallChickenEggIDs.Add(thisItemID);
                    }
                }
                if (thisItemID != id)
                {
                    List<string> newEggData = modData.customEggsAndHatchedAnimals[id];
                    modData.customEggsAndHatchedAnimals.Add(thisItemID, newEggData);
                    modData.customEggsAndHatchedAnimals.Remove(id);
                    SaveModData();
                }
            }
            SetUpEggData(smapiHelper);
        }
        private bool IsValidItemID(int IDtoValidate)
        {
            if(IDtoValidate > largestItemID && ((jaData.Count > 0 && !jaData.ContainsValue(IDtoValidate)) || jaData.Count <= 0))
            {
                return true;
            }
            return false;
        }
        public void SaveModData()
        {
            string serializedData = JsonConvert.SerializeObject(modData);
            string directoryPath = System.IO.Path.Combine(Constants.CurrentSavePath, dataFolderName);
            if (!System.IO.Directory.Exists(directoryPath))
            {
                System.IO.Directory.CreateDirectory(directoryPath);
            }
            System.IO.File.WriteAllText(dataFilePath, serializedData);
        }

        private void LoadModData()
        {
            dataFilePath = System.IO.Path.Combine(Constants.CurrentSavePath, dataFolderName, dataFileName);
            if (System.IO.File.Exists(dataFilePath))
            {
                string readData = System.IO.File.ReadAllText(dataFilePath);
                modData = JsonConvert.DeserializeObject<ModData>(readData);
            }
            if (modData == null)
            {
                modData = new ModData();
            }
        }
        public bool IsDeluxeEgg(int customEggIndex)
        {
            List<string> nonDeluxeKeywords = new List<string>
            {
                "false",
                "default",
                "small"
            };
            if(!modData.customEggsAndHatchedAnimals.ContainsKey(customEggIndex))
            {
                return false;
            } else if (!nonDeluxeKeywords.Contains(modData.customEggsAndHatchedAnimals[customEggIndex][modDataEggAspectIndex]))
            {
                return true;
            }
            return false;
        }
        public int DeluxeProductMultiplier(int customEggIndex)
        {
            int multiplier = 0;
            if (modData.customEggsAndHatchedAnimals.ContainsKey(customEggIndex))
            {
                string eggAspect = modData.customEggsAndHatchedAnimals[customEggIndex][modDataEggAspectIndex];
                if(eggAspect.Contains("(") && eggAspect.Contains(")"))
                {
                    string containedMultiplier = eggAspect.Substring(eggAspect.IndexOf("(") + 1);
                    containedMultiplier = containedMultiplier.Substring(0, containedMultiplier.IndexOf(")"));
                    int.TryParse(containedMultiplier, out multiplier);
                }
            }
            return multiplier;
        }

        public void Edit<T>(IAssetData asset)
        {
            if (asset.AssetNameEquals(System.IO.Path.Combine("Data", "ObjectInformation")))
            {
                IDictionary<int, string> data = asset.AsDictionary<int, string>().Data;
                if (Constants.CurrentSavePath == null)
                {
                    List<int> dataKeys = new List<int>(data.Keys);
                    dataKeys.Sort();
                    largestItemID = dataKeys[dataKeys.Count - 1];
                }
                else
                {
                    string smallEggData = "Egg/{{sell price}}/10/Basic -5/Egg/A{{species}} egg.";
                    string largeEggData = "Large Egg/{{sell price}}/15/Basic -5/Large Egg/It's an uncommonly large {{species}} egg!";
                    string eggData;
                    foreach (int id in modData.customEggsAndHatchedAnimals.Keys)
                    {
                        string speciesName = modData.customEggsAndHatchedAnimals[id][modDataBreedIndex].ToLower();
                        if (IsDeluxeEgg(id))
                        {
                            eggData = largeEggData;
                            if(modData.customEggsAndHatchedAnimals[id][modDataEggAspectIndex] != "true")
                            {
                                eggData = eggData.Replace("large", modData.customEggsAndHatchedAnimals[id][modDataEggAspectIndex]);
                            }
                            int multiplier = DeluxeProductMultiplier(id);
                            if(multiplier > 1)
                            {
                                eggData = eggData.Replace("Large Egg", "Egg x" + multiplier);
                            }
                        }
                        else
                        {
                            eggData = smallEggData;
                            if ("aeiouAEIOU".IndexOf(speciesName[0]) >= 0)
                            {
                                speciesName = "n " + speciesName;
                            }
                            else
                            {
                                speciesName = " " + speciesName;
                            }
                        }
                        eggData = eggData.Replace("{{species}}", speciesName).Replace("{{sell price}}", modData.customEggsAndHatchedAnimals[id][modDataPriceIndex]);

                        if (!data.ContainsKey(id))
                        {
                            data.Add(id, eggData);
                        }
                    }
                }
            }
            else if (chickenContentHandled && asset.AssetNameEquals(System.IO.Path.Combine("Data", "FarmAnimals")))
            {
                EditFarmAnimalsData(asset);
            }
            else if (eggContentHandled && asset.AssetNameEquals(System.IO.Path.Combine("Maps", "springobjects")))
            {
                foreach (int itemID in customEggTextures.Keys)
                {
                    Vector2 spriteSheetCoords = GetSpritesheetCoordsFromItemID(itemID);

                    IAssetDataForImage editor = asset.AsImage();
                    Texture2D eggTexture = smapiHelper.Content.Load<Texture2D>(customEggTextures[itemID], ContentSource.GameContent);
                    Rectangle fromArea = new Rectangle(0, 0, 16, 16);
                    Rectangle targetArea = new Rectangle((int)spriteSheetCoords.X, (int)spriteSheetCoords.Y, 16, 16);

                    // extend tilesheet if needed
                    if (targetArea.Bottom > editor.Data.Height)
                    {
                        Texture2D original = editor.Data;
                        Texture2D texture = new Texture2D(Game1.graphics.GraphicsDevice, original.Width, targetArea.Bottom);
                        editor.ReplaceWith(texture);
                        editor.PatchImage(original);
                    }

                    editor.PatchImage(eggTexture, fromArea, targetArea, PatchMode.Replace);
                }
            }
        }
        public static int GetItemIDFromSpritesheetCoords(Rectangle objectCoords)
        {
            return (int)((objectCoords.Y / 16) * 24 + (objectCoords.X / 16));
        }
        public static Vector2 GetSpritesheetCoordsFromItemID(int itemID)
        {
            return new Vector2((itemID % 24) * 16, (float)Math.Floor(itemID / 24f) * 16);
        }

        private void EditFarmAnimalsData(IAssetData asset)
        {
            IDictionary<string, string> data = asset.AsDictionary<string, string>().Data;
            string baseChickenData = "1/3/{{smallEggID}}/{{largeEggID}}/{{animalSound}}/8/32/48/32/8/32/48/32/0/false/Coop/16/16/16/16/4/7/null/641/800/{{animalType}}/Coop";
            int i = 1;
            int defaultProductID = -1;
            int deluxeProductID = -1;
            string[] animalColors = new string[] { "" };
            string species = "";
            string animalSound = "cluck";
            List<int> eggIDs = new List<int>(modData.customEggsAndHatchedAnimals.Keys);
            eggIDs.Sort();
            foreach (int eggID in eggIDs)
            {
                string[] newAnimalColors = modData.customEggsAndHatchedAnimals[eggID][modDataColorsIndex].Split(new string[] { ", " }, StringSplitOptions.None);
                string newSpecies = modData.customEggsAndHatchedAnimals[eggID][modDataBreedIndex];
                string newSound = modData.customEggsAndHatchedAnimals[eggID][modDataSoundIndex];
                if(species != "" && newSpecies != species)
                {
                    AddToFarmAnimals(data, baseChickenData, defaultProductID, deluxeProductID, animalColors, species, animalSound);
                    defaultProductID = -1;
                    deluxeProductID = -1;
                }
                species = newSpecies;
                animalColors = newAnimalColors;
                animalSound = newSound;
                if (IsDeluxeEgg(eggID))
                {
                    deluxeProductID = eggID;
                    int deluxeMultiplier = DeluxeProductMultiplier(eggID);
                    if (deluxeMultiplier != 0)
                    {
                        eggMultipliers.Add(eggID, new Vector2(defaultProductID, deluxeMultiplier));
                    }
                }
                else
                {
                    defaultProductID = eggID;
                }
                i++;
            }
            if (defaultProductID != -1 && species != "")
            {
                AddToFarmAnimals(data, baseChickenData, defaultProductID, deluxeProductID, animalColors, species, animalSound);
            }
        }

        private void AddToFarmAnimals(IDictionary<string, string> data, string baseData, int defaultProductID, int deluxeProductID, string[] animalColors, string species, string animalSound)
        {
            if(deluxeProductID == -1)
            {
                deluxeProductID = defaultProductID;
            }
            if (config.birdsLayBaseGameEggs)
            {
                if (rand.Next(0, 1) == 0)
                {
                    defaultProductID = 176;
                    deluxeProductID = 174;
                }
                else
                {
                    defaultProductID = 180;
                    deluxeProductID = 182;
                }
            }
            foreach (string animalColor in animalColors)
            {
                if (!data.ContainsKey(animalColor + species))
                {
                    string chickenData = baseData.Replace("{{animalType}}", animalColor + species).Replace("{{smallEggID}}", defaultProductID.ToString()).Replace("{{largeEggID}}", deluxeProductID.ToString());
                    if (animalSound != "cluck" && !soundEffects.ContainsKey(animalSound))
                    {
                        animalSound = "cluck";
                    }
                    chickenData = chickenData.Replace("{{animalSound}}", animalSound);
                    data.Add(animalColor + species, chickenData);
                }
            }
        }

        public T Load<T>(IAssetInfo asset)
        {
            if (asset.AssetName.StartsWith("Animals"))
            {
                string[] splitAssetName = asset.AssetName.Split(System.IO.Path.DirectorySeparatorChar);
                string chickenType = splitAssetName[splitAssetName.Length - 1];
                int isBaby = 0;
                if (chickenType.StartsWith("Baby"))
                {
                    isBaby = 1;
                    chickenType = chickenType.Substring("Baby".Length);
                }
                if (customChickenTextures.ContainsKey(chickenType))
                {
                    T animalTexture;
                    string texturePath = customChickenTextures[chickenType][isBaby];
                    if (!animalSourcePack.ContainsKey(chickenType))
                    {
                        animalTexture = smapiHelper.Content.Load<T>(texturePath, ContentSource.ModFolder);
                    }
                    else
                    {
                        IContentPack contentPack = contentPackNames[animalSourcePack[chickenType]];
                        animalTexture = contentPack.LoadAsset<T>(texturePath);
                    }
                    return animalTexture;
                }
            }
            else if (asset.AssetName.StartsWith("LooseSprites"))
            {
                string[] splitAssetName = asset.AssetName.Split(System.IO.Path.DirectorySeparatorChar);
                string eggType = splitAssetName[splitAssetName.Length - 1];
                T eggTexture;
                string texturePath = System.IO.Path.Combine("assets", "eggs", eggType + ".png");
                if (!eggSourcePack.ContainsKey(eggType))
                {
                    eggTexture = smapiHelper.Content.Load<T>(texturePath, ContentSource.ModFolder);
                } else
                {
                    IContentPack contentPack = contentPackNames[eggSourcePack[eggType]];
                    eggTexture = contentPack.LoadAsset<T>(texturePath);
                }
                return eggTexture;
            }
            return default(T);
        }

        /*
         * ======= BEGIN HARMONY PATCHES =======
         */
        void HarmonyPatches()
        {
            HarmonyInstance harmony = HarmonyInstance.Create("StardewValley.LavapulseChickens.Harmony");

            MethodInfo methodToPatch = AccessTools.Method(typeof(Coop), "dayUpdate");
            HarmonyMethod prefixMethod = new HarmonyMethod(typeof(ModEntry).GetMethod("BuildingDayUpdatePrefix"));
            harmony.Patch(methodToPatch, prefixMethod);

            methodToPatch = AccessTools.Method(typeof(AnimalHouse), "addNewHatchedAnimal");
            prefixMethod = new HarmonyMethod(typeof(ModEntry).GetMethod("NewHatchedAnimalPrefix"));
            harmony.Patch(methodToPatch, prefixMethod);

            methodToPatch = AccessTools.Method(typeof(AnimalHouse), "resetSharedState");
            prefixMethod = new HarmonyMethod(typeof(ModEntry).GetMethod("ResetSharedStatePrefix"));
            harmony.Patch(methodToPatch, prefixMethod);

            methodToPatch = AccessTools.Method(typeof(StardewValley.Object), "performObjectDropInAction");
            prefixMethod = new HarmonyMethod(typeof(ModEntry).GetMethod("DropInActionPrefix"));
            HarmonyMethod suffixMethod = new HarmonyMethod(typeof(ModEntry).GetMethod("DropInActionSuffix"));
            harmony.Patch(methodToPatch, prefixMethod, suffixMethod);

            methodToPatch = AccessTools.Method(typeof(FarmAnimal), "makeSound");
            prefixMethod = new HarmonyMethod(typeof(ModEntry).GetMethod("MakeSoundPrefix"));
            harmony.Patch(methodToPatch, prefixMethod);

            methodToPatch = AccessTools.Method(typeof(FarmAnimal), "pet");
            prefixMethod = new HarmonyMethod(typeof(ModEntry).GetMethod("PetPrefix"));
            harmony.Patch(methodToPatch, prefixMethod);
        }
        public static void PetPrefix(FarmAnimal __instance, ref Farmer who)
        {
            if (who.FarmerSprite.PauseForSingleAnimation)
                return;
            if ((bool)((NetFieldBase<bool, NetBool>)__instance.wasPet) && (who.ActiveObject == null || (int)((NetFieldBase<int, NetInt>)who.ActiveObject.parentSheetIndex) != 178))
            {
                PlayAnimalSound(__instance);
            }
        }
        public static void MakeSoundPrefix(FarmAnimal __instance)
        {
            PlayAnimalSound(__instance);
        }
        public static void PlayAnimalSound(FarmAnimal animal)
        {
            NetLong animalID = animal.myID;
            if (ModEntry.modifiedAnimalSounds.ContainsKey(animalID))
            {
                string soundName = ModEntry.modifiedAnimalSounds[animalID];
                if (ModEntry.modEntry.soundEffects.ContainsKey(soundName))
                {
                    int soundIndex = ModEntry.modEntry.rand.Next(0, ModEntry.modEntry.soundEffects[soundName].Count);
                    ModEntry.modEntry.soundEffects[soundName][soundIndex].Play();
                }
            }
        }
        public static void DropInActionPrefix(StardewValley.Object __instance, ref bool __result, ref bool __state, Item dropInItem, bool probe, Farmer who)
        {
            bool disqualify = false;
            if (__instance.isTemporarilyInvisible || !(dropInItem is StardewValley.Object))
                disqualify = true;
            StardewValley.Object object1 = dropInItem as StardewValley.Object;
            if (__instance.heldObject.Value != null && !__instance.name.Equals("Recycling Machine") && !__instance.name.Equals("Crystalarium") || object1 != null && (bool)((NetFieldBase<bool, NetBool>)object1.bigCraftable))
                disqualify = true;
            if (!disqualify && __instance.name.Equals("Mayonnaise Machine"))
            {
                int eggID = object1.ParentSheetIndex;
                if (ModEntry.IsCustomEgg(eggID))
                {
                    int qualityValue = 0;
                    if (ModEntry.modEntry.IsDeluxeEgg(eggID))
                    {
                        qualityValue = 2;
                    }
                    __instance.heldObject.Value = new StardewValley.Object(Vector2.Zero, 306, (string)null, false, true, false, false)
                    {
                        Quality = qualityValue
                    };
                    if (!probe)
                    {
                        __instance.minutesUntilReady.Value = 180;
                        who.currentLocation.playSound("Ship", StardewValley.Network.NetAudio.SoundContext.Default);
                    }
                    __result = true;
                    __state = true;
                }
            }
        }
        public static bool IsCustomEgg(int eggID)
        {
            if (Game1.objectInformation.ContainsKey(eggID))
            {
                string[] eggData = Game1.objectInformation[eggID].Split('/');
                if (eggData.Length > 3 && eggData[3] == "Basic -5" && (ModEntry.modEntry.modData.customEggsAndHatchedAnimals.ContainsKey(eggID) || !ModEntry.eggsAndHatchedAnimals.ContainsKey(eggID)))
                {
                    return true;
                }
            }
            return false;
        }
        public static void DropInActionSuffix(StardewValley.Object __instance, ref bool __result, ref bool __state, Item dropInItem, bool probe, Farmer who)
        {
            if (__state == true && __result == false)
            {
                __result = true;
            }
        }

        public static void ResetSharedStatePrefix(AnimalHouse __instance, Event __state)
        {
            foreach (StardewValley.Object @object in __instance.objects.Values)
            {
                if ((bool)((NetFieldBase<bool, NetBool>)@object.bigCraftable) && @object.Name.Contains("Incubator") && (@object.heldObject.Value != null && (int)((NetFieldBase<int, NetIntDelta>)@object.minutesUntilReady) <= 0) && !__instance.isFull())
                {
                    string str = "??";
                    int eggID = @object.heldObject.Value.ParentSheetIndex;
                    if (ModEntry.IsCustomEgg(eggID))
                    {
                        if (ModEntry.modEntry.modData.customEggsAndHatchedAnimals.ContainsKey(eggID)) {
                            string animalType = ModEntry.modEntry.modData.customEggsAndHatchedAnimals[eggID][modDataBreedIndex];
                            if (animalType.Contains("Void"))
                            {
                                str = Game1.content.LoadString("Strings\\Locations:AnimalHouse_Incubator_Hatch_VoidEgg");
                            }
                            else if (animalType.Contains("Dinosaur"))
                            {
                                str = Game1.content.LoadString("Strings\\Locations:AnimalHouse_Incubator_Hatch_DinosaurEgg");
                            }
                            else if (animalType.Contains("Duck"))
                            {
                                str = Game1.content.LoadString("Strings\\Locations:AnimalHouse_Incubator_Hatch_DuckEgg");
                            } else
                            {
                                str = Game1.content.LoadString("Strings\\Locations:AnimalHouse_Incubator_Hatch_RegularEgg");
                            }
                        } else
                        {
                            str = Game1.content.LoadString("Strings\\Locations:AnimalHouse_Incubator_Hatch_RegularEgg");
                        }

                        if (str != "??")
                        {
                            modEntry.eventText = str;
                            break;
                        }
                    }
                }
            }
        }
        public static void NewHatchedAnimalPrefix(AnimalHouse __instance, ref string name, ref string __state)
        {
            if (__instance.getBuilding() is Coop)
            {
                foreach (StardewValley.Object @object in __instance.objects.Values)
                {
                    if ((bool)((NetFieldBase<bool, NetBool>)@object.bigCraftable) && @object.Name.Contains("Incubator") && (@object.heldObject.Value != null && (int)((NetFieldBase<int, NetIntDelta>)@object.minutesUntilReady) <= 0) && !__instance.isFull())
                    {
                        FarmAnimal farmAnimal = ModEntry.BirthAnimal(__instance, @object.heldObject.Value.ParentSheetIndex);

                        if (farmAnimal != null)
                        {
                            farmAnimal.Name = name;
                            farmAnimal.displayName = name;
                            @object.heldObject.Value = (StardewValley.Object)null;
                            @object.ParentSheetIndex = 101;
                            __state = farmAnimal.type;
                        }
                        break;
                    }
                }
            }
        }
        public static void BuildingDayUpdatePrefix(Coop __instance, ref int dayOfMonth)
        {
            AnimalHouse animalHouseInstance = (AnimalHouse)__instance.indoors.Value;
            int eggID = animalHouseInstance.incubatingEgg.Y;
            FarmAnimal farmAnimal;
            // If egg is ready to hatch ...
            if (eggID > 0 && animalHouseInstance.incubatingEgg.X <= 0)
            {
                farmAnimal = ModEntry.BirthAnimal(animalHouseInstance, eggID);

                if (farmAnimal != null)
                {
                    __instance.indoors.Value.map.GetLayer("Front").Tiles[1, 2].TileIndex = 45;
                }
            }
        }
        public static int ConvertFromOldID (int oldID)
        {
            int newID = oldID;
            if (oldID - 52820 >= 0)
            {
                oldID -= 52820;
                List<int> customEggIDs = new List<int>(ModEntry.modEntry.modData.customEggsAndHatchedAnimals.Keys);
                if (customEggIDs.Count >= oldID)
                {
                    customEggIDs.Sort();
                    newID = customEggIDs[oldID];
                }
            }
            if(ModEntry.unloadedEggIDs.Count > 0 && ModEntry.unloadedEggIDs.Contains(oldID))
            {
                newID = 174;
            }
            return newID;
        }
        public static FarmAnimal BirthAnimal(AnimalHouse animalHouseInstance, int eggID)
        {
            FarmAnimal farmAnimal = null;

            // ... then hatch egg before game can.
            long newId = smapiHelper.Multiplayer.GetNewID();
            if (ModEntry.IsCustomEgg(eggID))
            {
                string type = "White Chicken";
                if (!ModEntry.modEntry.modData.customEggsAndHatchedAnimals.ContainsKey(eggID))
                {
                    // Attempt to convert chicken eggs from old ID system
                    eggID = ModEntry.ConvertFromOldID(eggID);
                }
                if (ModEntry.modEntry.modData.customEggsAndHatchedAnimals.ContainsKey(eggID))
                {
                    type = eggsAndHatchedAnimals[eggID];
                    // Allow for variants
                    string[] splitType = type.Split(new string[] { ", " }, StringSplitOptions.None);
                    if (splitType.Length > 1)
                    {
                        type = splitType[ModEntry.modEntry.rand.Next(0, splitType.Length)];
                    }
                }

                farmAnimal = new FarmAnimal(type, newId, (long)Game1.player.uniqueMultiplayerID);
                if (ModEntry.modEntry.customChickenTextures.ContainsKey(type))
                {
                    farmAnimal.type.Value = type;
                    farmAnimal.displayType = null;
                    farmAnimal.reloadData();
                    ModEntry.modEntry.ReplaceCustomAnimalSound(farmAnimal);
                }

                animalHouseInstance.incubatingEgg.X = 0;
                animalHouseInstance.incubatingEgg.Y = -1;

                animalHouseInstance.animals.Add(farmAnimal.myID, farmAnimal);
                animalHouseInstance.animalsThatLiveHere.Add((long)farmAnimal.myID);

                Building building = animalHouseInstance.getBuilding();
                if (building != null)
                {
                    farmAnimal.home = building;
                    farmAnimal.homeLocation.Value = new Vector2((float)(int)((NetFieldBase<int, NetInt>)building.tileX), (float)(int)((NetFieldBase<int, NetInt>)building.tileY));
                    farmAnimal.setRandomPosition((GameLocation)((NetFieldBase<GameLocation, NetRef<GameLocation>>)farmAnimal.home.indoors));
                }
            }
            return farmAnimal;
        }

        /*
         * ======= END HARMONY PATCHES =======
         */

        /*
         * ======= BEGIN SMAPI EVENT HOOKS =======
         */
        void SMAPIEventHooks()
        {
            smapiHelper.Events.Display.MenuChanged += this.OnMenuChanged;
            smapiHelper.Events.Specialized.LoadStageChanged += this.OnLoadStageChanged;
            smapiHelper.Events.Player.InventoryChanged += this.OnInventoryChanged;
            smapiHelper.Events.GameLoop.DayStarted += this.OnDayStarted;
        }
        private void OnInventoryChanged(object sender, InventoryChangedEventArgs e)
        {
            foreach (Item itemAdded in e.Added)
            {
                if (eggMultipliers.ContainsKey(itemAdded.parentSheetIndex))
                {
                    Game1.player.removeItemFromInventory(Game1.player.getIndexOfInventoryItem(itemAdded));
                    Game1.player.addItemToInventory(new StardewValley.Object((int)eggMultipliers[itemAdded.parentSheetIndex].X, (int)eggMultipliers[itemAdded.parentSheetIndex].Y, quality: (itemAdded as StardewValley.Object).quality));
                }
                else
                {
                    int convertedID = ConvertFromOldID(itemAdded.parentSheetIndex);
                    if (itemAdded.parentSheetIndex != convertedID)
                    {
                        Game1.player.removeItemFromInventory(Game1.player.getIndexOfInventoryItem(itemAdded));
                        Game1.player.addItemToInventory(new StardewValley.Object(convertedID, itemAdded.Stack, quality: (itemAdded as StardewValley.Object).quality));
                    }
                }
            }
        }

        private void OnLoadStageChanged(object sender, LoadStageChangedEventArgs e)
        {
            if (e.NewStage == StardewModdingAPI.Enums.LoadStage.SaveParsed)
            {
                LoadModData();
                LoadContentPacks();
                SetUpChickenData();
                string jaObjectsPath = System.IO.Path.Combine(Constants.CurrentSavePath, "JsonAssets", "ids-objects.json");
                if (System.IO.File.Exists(jaObjectsPath))
                {
                    jaData = JsonConvert.DeserializeObject<Dictionary<string, int>>(System.IO.File.ReadAllText(jaObjectsPath));
                }
                FindIDsForEggs();
            }
        }

        private void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            ReplaceCustomAnimalSounds();
        }

        private void OnMenuChanged(object sender, MenuChangedEventArgs e)
        {
            if (e != null)
            {
                currentMenu = e.NewMenu;
                if (currentMenu != null)
                {
                    if (currentMenu is StardewValley.Menus.ShopMenu)
                    {
                        StardewValley.Menus.ShopMenu shopMenu = (StardewValley.Menus.ShopMenu)currentMenu;

                        if (shopMenu != null && shopMenu.portraitPerson != null && shopMenu.portraitPerson.Name == "Marnie")
                        {
                            IReflectedField<Dictionary<ISalable, int[]>> inventoryInformation = Helper.Reflection.GetField<Dictionary<ISalable, int[]>>(shopMenu, "itemPriceAndStock");
                            Dictionary<ISalable, int[]> itemPriceAndStock = null;
                            if (inventoryInformation != null)
                            {
                                itemPriceAndStock = inventoryInformation.GetValue();
                            }
                            IReflectedField<List<ISalable>> forSaleInformation = Helper.Reflection.GetField<List<ISalable>>(shopMenu, "forSale");
                            List<ISalable> forSale = null;
                            if (forSaleInformation != null)
                            {
                                forSale = forSaleInformation.GetValue();
                            }

                            if (forSale != null && itemPriceAndStock != null)
                            {
                                Item milk = new StardewValley.Object(184, config.numMilkToStock, false, -1, 0);
                                itemPriceAndStock.Add(milk, new[] { milk.salePrice() * 3, milk.Stack });
                                if (!forSale.Contains(milk))
                                    forSale.Add(milk);

                                List<Item> eggsToAdd = GetEggsOfTheDay();
                                if (eggsToAdd != null)
                                {
                                    foreach (Item eggToAdd in eggsToAdd)
                                    {
                                        itemPriceAndStock.Add(eggToAdd, new[] { eggToAdd.salePrice() * 5, eggToAdd.Stack });
                                        if (!forSale.Contains(eggToAdd))
                                            forSale.Add(eggToAdd);
                                    }
                                }

                                inventoryInformation.SetValue(itemPriceAndStock);
                                forSaleInformation.SetValue(forSale);
                            }
                        }
                    }
                    else if (currentMenu is StardewValley.Menus.DialogueBox)
                    {
                        StardewValley.Menus.DialogueBox dialogueBox = (StardewValley.Menus.DialogueBox)currentMenu;
                        if (eventText != "")
                        {
                            if (dialogueBox != null && dialogueBox.getCurrentString() == "??")
                            {
                                string eventTextClone = eventText;
                                Game1.drawObjectDialogue(eventTextClone);
                            }
                            eventText = "";
                        }
                    }
                }
            }
        }
        private List<Item> GetEggsOfTheDay()
        {
            int today = Game1.dayOfMonth;
            if(eggsOfTheDay.Count <= 0 || today != dailyEggsLastUpdated)
            {
                eggsOfTheDay.Clear();
                Dictionary<int, int> eggsStock = new Dictionary<int, int>();
                for(int i = 0; i < config.numEggsToStock; i++)
                {
                    int eggIndex = rand.Next(0, smallChickenEggIDs.Count);
                    int eggID = smallChickenEggIDs[eggIndex];
                    if (eggsStock.ContainsKey(eggID))
                    {
                        eggsStock[eggID]++;
                    } else
                    {
                        eggsStock.Add(eggID, 1);
                    }
                }
                foreach(KeyValuePair<int, int> eggStock in eggsStock)
                {
                    eggsOfTheDay.Add(new StardewValley.Object(eggStock.Key, eggStock.Value, false, -1, 0));
                }
                dailyEggsLastUpdated = today;
            }
            return eggsOfTheDay;
        }

        /*
         * ======= END SMAPI EVENT HOOKS =======
         */
    }
}