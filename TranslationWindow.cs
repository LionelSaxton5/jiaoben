using Godot;
using System;

public partial class TranslationWindow : Window //翻译窗口
{
	private bool isDragging = false; //是否正在拖拽窗口
	private Vector2 dragOffset; //拖拽偏移量

    // 调整大小相关变量
    private int resizeBorderThickness = 5; //调整大小边框厚度
	private bool isResizing = false; // 是否正在调整大小
	private ResizeDirection resizeDirection = ResizeDirection.None; // 当前调整方向
    private Vector2I originalPosition; // 记录开始调整时的窗口位置
    private Vector2I originalSize;     // 记录开始调整时的窗口大小

    public TranslationResult translationResult; //翻译结果节点

	private enum ResizeDirection //调整方向
	{
		None,
		Left,
		Right,
		Top,
		Bottom,
		TopLeft,
		TopRight,
		BottomLeft,
		BottomRight
	}

	public override void _Ready()
	{
		var translationResultScene = GD.Load<PackedScene>("res://changjing/TranslationResultUI.tscn");
		translationResult = translationResultScene.Instantiate<TranslationResult>();

		AddChild(translationResult); //添加翻译结果节点	

		translationResult.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); //设置填满窗口
		translationResult.Position = Vector2.Zero;

		translationResult.SetParentWindow(this); //设置父窗口引用
	}

	public override void _Process(double delta)
	{
		// 检查鼠标位置并设置光标形状
		Vector2 mousePos = DisplayServer.MouseGetPosition();
		Rect2I windowRect = new Rect2I(Position, Size);
		
		if (windowRect.HasPoint((Vector2I)mousePos))
		{
			ResizeDirection dir = GetResizeDirection(mousePos);
			if (dir != ResizeDirection.None)
			{
				DisplayServer.CursorSetShape(GetCursorShapeForDirection(dir));
			}
			else
			{
				DisplayServer.CursorSetShape(DisplayServer.CursorShape.Arrow);
			}
		}
		else
		{
			DisplayServer.CursorSetShape(DisplayServer.CursorShape.Arrow);
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton)
		{
			//GD.Print($"TranslationWindow: ButtonIndex={mouseButton.ButtonIndex}, Pressed={mouseButton.Pressed}, Position={mouseButton.Position}, GlobalPosition={mouseButton.GlobalPosition}");
			if (mouseButton.ButtonIndex == MouseButton.Left)
			{
				if (mouseButton.Pressed)
				{
					var localPos = mouseButton.Position;

					if (localPos.X >= 0 && localPos.X <= Size.X &&
						localPos.Y >= 0 && localPos.Y <= Size.Y)
					{
                        resizeDirection = GetResizeDirection(mouseButton.GlobalPosition);

						if (resizeDirection == ResizeDirection.None)
						{
							isDragging = true;
							dragOffset = mouseButton.GlobalPosition - (Vector2)Position; //记录鼠标按下时的偏移量
                        }
						else
						{
                            isResizing = true;                            
                            originalPosition = Position;
                            originalSize = Size;
                            dragOffset = mouseButton.GlobalPosition; //记录鼠标按下时的位置
                        }
					}
				}
				else
				{
					isDragging = false; //停止拖拽
					isResizing = false; //停止调整大小
                    resizeDirection = ResizeDirection.None;
                }
			}
		}
		else if (@event is InputEventMouseMotion mouseMotion)
		{
            if (isResizing)
			{
				ResizeWindow(mouseMotion.GlobalPosition);
            }
            else if (isDragging)
			{
				Position = (Vector2I)(mouseMotion.GlobalPosition - dragOffset); //更新窗口位置
			}
		}
	}

	private ResizeDirection GetResizeDirection(Vector2 mousePos) //调整边框大小
	{
		Rect2I rect2I = new Rect2I(Position, Size);

        if (rect2I.HasPoint((Vector2I)mousePos))
		{
			//检测鼠标位置以确定调整方向
			if (Mathf.Abs(mousePos.X - rect2I.Position.X) <= resizeBorderThickness && Mathf.Abs(mousePos.Y - rect2I.Position.Y) > resizeBorderThickness && Mathf.Abs(mousePos.Y - (rect2I.Position.Y + rect2I.Size.Y)) > resizeBorderThickness)
			{
				resizeDirection = ResizeDirection.Left; //左
			}
			else if (Mathf.Abs(mousePos.X - (rect2I.Position.X + rect2I.Size.X)) <= resizeBorderThickness && Mathf.Abs(mousePos.Y - rect2I.Position.Y) > resizeBorderThickness && Mathf.Abs(mousePos.Y - (rect2I.Position.Y + rect2I.Size.Y)) > resizeBorderThickness)
			{
				resizeDirection = ResizeDirection.Right; //右
			}
			else if (Mathf.Abs(mousePos.Y - rect2I.Position.Y) <= resizeBorderThickness && Mathf.Abs(mousePos.X - rect2I.Position.X) > resizeBorderThickness && Mathf.Abs(mousePos.X - (rect2I.Position.X + rect2I.Size.X)) > resizeBorderThickness)
			{
				resizeDirection = ResizeDirection.Top;  //上
			}
			else if (Mathf.Abs(mousePos.Y - (rect2I.Position.Y + rect2I.Size.Y)) <= resizeBorderThickness && Mathf.Abs(mousePos.X - rect2I.Position.X) > resizeBorderThickness && Mathf.Abs(mousePos.X - (rect2I.Position.X + rect2I.Size.X)) > resizeBorderThickness)
			{
				resizeDirection = ResizeDirection.Bottom; //下
			}
			else if (Mathf.Abs(mousePos.X - rect2I.Position.X) <= resizeBorderThickness && Mathf.Abs(mousePos.Y - rect2I.Position.Y) <= resizeBorderThickness)
			{
				resizeDirection = ResizeDirection.TopLeft; //左上
			}
			else if (Mathf.Abs(mousePos.X - (rect2I.Position.X + rect2I.Size.X)) <= resizeBorderThickness && Mathf.Abs(mousePos.Y - rect2I.Position.Y) <= resizeBorderThickness)
			{
				resizeDirection = ResizeDirection.TopRight; //右上
			}
			else if (Mathf.Abs(mousePos.X - rect2I.Position.X) <= resizeBorderThickness && Mathf.Abs(mousePos.Y - (rect2I.Position.Y + rect2I.Size.Y)) <= resizeBorderThickness)
			{
				resizeDirection = ResizeDirection.BottomLeft; //左下
			}
			else if (Mathf.Abs(mousePos.X - (rect2I.Position.X + rect2I.Size.X)) <= resizeBorderThickness && Mathf.Abs(mousePos.Y - (rect2I.Position.Y + rect2I.Size.Y)) <= resizeBorderThickness)
			{
				resizeDirection = ResizeDirection.BottomRight; //右下
			}
		}
        return resizeDirection;	
    }

	private void ResizeWindow(Vector2 mousePos)
	{
        Vector2 delta = mousePos - dragOffset; //鼠标移动的距离

        //根据调整方向调整窗口大小
        Vector2 newPos = Position;
        Vector2I newSize = Size;

		switch (resizeDirection)
		{
			case ResizeDirection.Left:
                // 左边调整：改变窗口左边和宽度
                newPos.X = (int)(originalPosition.X + delta.X);
                newSize.X = (int)(originalSize.X - delta.X);
                break;
			case ResizeDirection.Right:
                // 右边调整：只改变宽度
                newSize.X = (int)(originalSize.X + delta.X);
                break;
			case ResizeDirection.Top:
                // 上边调整：改变窗口上边和高度
                newPos.Y = (int)(originalPosition.Y + delta.Y);
                newSize.Y = (int)(originalSize.Y - delta.Y);
                break;
            case ResizeDirection.Bottom:
                // 下边调整：只改变高度
                newSize.Y = (int)(originalSize.Y + delta.Y);
                break;
            case ResizeDirection.TopLeft:
                // 左上角调整：同时改变位置和大小
                newPos.X = (int)(originalPosition.X + delta.X);
                newPos.Y = (int)(originalPosition.Y + delta.Y);
                newSize.X = (int)(originalSize.X - delta.X);
                newSize.Y = (int)(originalSize.Y - delta.Y);
                break;
            case ResizeDirection.TopRight:
                // 右上角调整：改变上边位置、宽度和高度
                newPos.Y = (int)(originalPosition.Y + delta.Y);
                newSize.X = (int)(originalSize.X + delta.X);
                newSize.Y = (int)(originalSize.Y - delta.Y);
                break;
            case ResizeDirection.BottomLeft:
                // 左下角调整：改变左边位置、宽度和高度
                newPos.X = (int)(originalPosition.X + delta.X);
                newSize.X = (int)(originalSize.X - delta.X);
                newSize.Y = (int)(originalSize.Y + delta.Y);
                break;
            case ResizeDirection.BottomRight:
                // 右下角调整：只改变宽度和高度
                newSize.X = (int)(originalSize.X + delta.X);
                newSize.Y = (int)(originalSize.Y + delta.Y);
                break;
        }

        // 限制最小大小
        newSize.X = Mathf.Max(newSize.X, 100);
        newSize.Y = Mathf.Max(newSize.Y, 100);

        // 确保调整后位置有效（不能为负数）
        newPos.X = Mathf.Max(newPos.X, 0);
        newPos.Y = Mathf.Max(newPos.Y, 0);

		Position = (Vector2I)newPos;
		Size = newSize;
    }

	private DisplayServer.CursorShape GetCursorShapeForDirection(ResizeDirection dir) //获取光标形状
    {
		switch (dir)
		{
			case ResizeDirection.Left:
			case ResizeDirection.Right:
				return DisplayServer.CursorShape.Hsize;
			case ResizeDirection.Top:
			case ResizeDirection.Bottom:
				return DisplayServer.CursorShape.Vsize;
			case ResizeDirection.TopLeft:
			case ResizeDirection.BottomRight:
				return DisplayServer.CursorShape.Fdiagsize;
			case ResizeDirection.TopRight:
			case ResizeDirection.BottomLeft:
				return DisplayServer.CursorShape.Bdiagsize;
			default:
				return DisplayServer.CursorShape.Arrow;
		}
	}
}
