using Godot;
using System;

public partial class ProgressWindow : Window
{
	private ProgressBar progressBar; //进度条
	private Label label2; //翻译完成标签
	private TextureRect texture;
	private TextureRect texture2;

    public override void _Ready()
	{
		progressBar = GetNode<ProgressBar>("Control/ProgressBar"); //获取ProgressBar节点
		label2 = GetNode<Label>("Control/Label2"); //获取Label节点
		texture = GetNode<TextureRect>("Control/TextureRect"); //获取TextureRect1节点
        texture2 = GetNode<TextureRect>("Control/TextureRect2"); //获取TextureRect2节点

        label2.Visible = false; //初始时隐藏标签
		texture2.Visible = false; //初始时隐藏TextureRect2


        CloseRequested += _Exit; //订阅窗口关闭事件
    }

	public void OnProgressBarValueChanged(int value, int maxvalue) //进度条值改变时调用
	{
		progressBar.Value = value; //更新进度条的值
		progressBar.MaxValue = maxvalue; //更新进度条的最大值

		if (value >= maxvalue) //如果进度完成
		{
			ShowLabelText();
		}
    }

	public void ShowLabelText() //设置标签文本
	{
		label2.Visible = true;
		texture.Visible = false; 
		texture2.Visible = true; 
    }

	private void _Exit()
	{
		this.QueueFree(); //释放窗口资源
    }
}
