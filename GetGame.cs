using Godot;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

public partial class GetGame : Node //获取边狱巴士游戏路径
{
	public static readonly Dictionary<string, int> STEAM_APP_IDS = new Dictionary<string, int>
	{
		{ "Limbus Company", 1973530 }
	};

	public string gameInstallPath = ""; //游戏安装路径

    //章节选择
    private int selectedChapter = 9; //默认章节9
    private int selectedLevel = 1;    //默认关卡1

    public override void _Ready()
	{
		gameInstallPath = GetGameInstallPath("Limbus Company");
    }


	public override void _Process(double delta)
	{
	}

	public void OnChapterValueChanged(int chapter)
	{
		selectedChapter = chapter;
		GD.Print($"选择的章节: {selectedChapter}");
    }
	public void OnLevelValueChanged(int level)
	{
		selectedLevel = level;
		GD.Print($"选择的关卡: {selectedLevel}");
    }

    public static string GetSteamInstallPath() //获取Steam安装路径
    {
		using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")) //打开注册表键
		{
			if (key != null)
			{
				object value = key.GetValue("SteamPath"); //获取Steam安装路径
				if (value != null)
				{
					return value.ToString();
                }
            }
		}
		return null; //未找到Steam安装路径
    }

	public static string GetGameInstallPath(string gameKey)
	{
		if (!STEAM_APP_IDS.ContainsKey(gameKey))
		{
			GD.Print($"游戏键 '{gameKey}' 不存在");
			return null;
		}

		string steamPath = GetSteamInstallPath();
        if (steamPath == null)
        {
            return null;
        }

		string libraryPath = steamPath; //默认Steam库路径
		int appId = STEAM_APP_IDS[gameKey]; //获取游戏的Steam应用ID(获取对应键的值)
        string gamePath = System.IO.Path.Combine(libraryPath, "steamapps", "common", gameKey); //构建游戏路径

		if (System.IO.Directory.Exists(gamePath))
		{
			GD.Print($"找到游戏路径: {gamePath}");
            return gamePath; //返回游戏路径
		}
		else
		{
			GD.Print($"未找到游戏路径: {gamePath}");
			return null; //未找到游戏路径
        }
    }

	public void OperateFile() //操作文件
    {
        //复制语言文件,改名
        string Document = System.IO.Path.Combine(gameInstallPath, "LimbusCompany_Data", "Lang", "Jp_zh-cn"); //目标文件路径

		if (!Directory.Exists(Document)) //检查目标文件是否存在
		{
			string filePath = System.IO.Path.Combine(gameInstallPath, "LimbusCompany_Data", "Assets", "Resources_moved", "Localize", "jp"); //源文件路径
			string destinationPath = System.IO.Path.Combine(gameInstallPath, "LimbusCompany_Data", "Lang", "jp"); //目标文件路径

			if (!Directory.Exists(destinationPath))
			{
				File.Copy(filePath, destinationPath, true); //复制文件并覆盖
			}

            var dirInfo = new DirectoryInfo(filePath); //获取源文件夹信息
            dirInfo.MoveTo(Document); //重命名文件夹
        }

        //复制字体文件
        string fontDocument = System.IO.Path.Combine(Document, "Font"); //字体文件路径
		if (!Directory.Exists(fontDocument)) //检查字体文件夹是否存在
		{
            string projectRootPath = ProjectSettings.GlobalizePath("res://");
            string fontSourcePath = System.IO.Path.Combine(projectRootPath, "Font");
            string exePath = OS.GetExecutablePath();  //获取Godot可执行文件路径
            string exeDir = System.IO.Path.GetDirectoryName(exePath); //获取Godot可执行文件目录


            string[] possibleFontPaths =
			{
				System.IO.Path.Combine(exeDir,"Font"),
                System.IO.Path.Combine(projectRootPath, "Font"),
				System.IO.Path.Combine(fontSourcePath),
                System.IO.Path.Combine(exeDir,"..","Font"),

                @"D:\xiazai\Godot sucai\边狱翻译器\Font" //开发环境
            }; //源字体文件路径

			string fontSource = null;
			foreach (var path in possibleFontPaths)
			{
				if (System.IO.Directory.Exists(path))
				{
					fontSource = path;
					GD.Print($"找到字体文件路径: {fontSource}");
					break;
                }
            }

            string fontDestinationPath = System.IO.Path.Combine(Document, "Font"); //目标字体文件路径
			if (!Directory.Exists(fontDestinationPath))
			{
				File.Copy(fontSource, fontDestinationPath, true); //复制字体文件并覆盖
			}
        }

        //修改josn文件
		string jsonFilePath = System.IO.Path.Combine(gameInstallPath, "LimbusCompany_Data", "Lang", "config.json");
		if (!File.Exists(jsonFilePath))
		{
			GD.PrintErr($"配置文件不存在: {jsonFilePath}");
			return;
		}
		string jsonContent = File.ReadAllText(jsonFilePath); //读取JSON文件内容
		var jsonData = System.Text.Json.JsonDocument.Parse(jsonContent).RootElement; //解析JSON内容
		if (jsonData.TryGetProperty("lang", out var languageProperty))
		{
			var newJson = JsonSerializer.Serialize(new { lang = "Jp_zh-cn" }); ; //创建新的JSON内容
			File.WriteAllText(jsonFilePath, newJson); //将修改后的内容写回文件
        }
        else
        {
            GD.Print("JSON中未找到 'lang' 键");
        }    
	}

	private void GetOriginalText()
	{
		
    }

}
