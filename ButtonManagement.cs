using Godot;
using System;

public partial class ButtonManagement : VBoxContainer //按钮管理容器
{
	private Button startButton; //启动按钮
    private Button timeTranslationButton; //实时翻译按钮
	private Button warehouseButton; //翻译源仓库按钮
    private Button embeddedButton; //嵌入按钮
	private Button oCRButton; //OCR设置按钮

	//UI
	private Panel startPanel;
	private Panel warehousePanel;
	private Panel embeddedPanel;
    private Panel oCRPanel;

	//汉化选择按钮
	private SpinBox chapterSpinBox;
	private SpinBox levelSpinBox;

	private GetGame getGame; //获取游戏节点

    public override void _Ready()
	{
		startButton = GetNode<Button>("StartButton"); //获取启动按钮
        timeTranslationButton = GetNode<Button>("TranslateButton"); //获取实时翻译按钮
		warehouseButton = GetNode<Button>("WarehouseButton"); //获取翻译源仓库按钮
        embeddedButton = GetNode<Button>("EmbeddedButton"); //获取嵌入按钮
		oCRButton = GetNode<Button>("OCRButton"); //获取OCR设置按钮

		startPanel = GetNode<Panel>("StartButton/StartPanel");
        embeddedPanel = GetNode<Panel>("EmbeddedButton/EmbeddedPanel");
		oCRPanel = GetNode<Panel>("OCRButton/OCRPanel");

		chapterSpinBox = GetNode<SpinBox>("EmbeddedButton/EmbeddedPanel/ChapterSpinBox");
		levelSpinBox = GetNode<SpinBox>("EmbeddedButton/EmbeddedPanel/LevelSpinBox");

        var inlineScene = GD.Load<PackedScene>("res://changjing/InlineTranslation.tscn").Instantiate();
		getGame = inlineScene.GetNode<GetGame>("GetGame");

        //连接选择汉化章节和关卡的事件
        chapterSpinBox.ValueChanged += (double value) => getGame.OnChapterValueChanged((int)value);
		levelSpinBox.ValueChanged += (double value) => getGame.OnLevelValueChanged((int)value);

        startPanel.Visible = true;
		embeddedPanel.Visible = false;
		oCRPanel.Visible = false;
    }


	public override void _Process(double delta)
	{
	}

	private void OnStartButtonPressed() //启动按钮按下事件
	{
		startPanel.Visible = true;
		//warehousePanel.Visible = false;
		embeddedPanel.Visible = false;
		oCRPanel.Visible = false;
    }

	private void OnWarehouseButtonPressed() //翻译源仓库按钮按下事件
	{
		startPanel.Visible = false;
		//warehousePanel.Visible = true;
		embeddedPanel.Visible = false;
		oCRPanel.Visible = false;
    }

	private void OnEmbeddedButtonPressed() //嵌入按钮按下事件
	{
        startPanel.Visible = false;
        //warehousePanel.Visible = false;
        embeddedPanel.Visible = true;
        oCRPanel.Visible = false;
    }

	private void OnOCRButtonPressed() //OCR设置按钮按下事件
	{
		GD.Print("OCR设置按钮被按下");
        startPanel.Visible = false;
        //warehousePanel.Visible = false;
		embeddedPanel.Visible = false;
		oCRPanel.Visible = true;
    }

}
