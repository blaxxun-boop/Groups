using UnityEngine;
using UnityEngine.EventSystems;

namespace Groups;

public class DragNDrop : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
	public RectTransform target = null!;
	public bool shouldReturn;
	private bool isMouseDown;
	private Vector3 startMousePosition;
	private Vector3 startPosition;

	private void Awake()
	{
		target = (RectTransform)transform;
	}

	private void Update()
	{
		if (isMouseDown)
		{
			Vector3 currentPosition = Input.mousePosition;
			Vector3 diff = currentPosition - startMousePosition;
			Vector3 pos = startPosition + diff;

			SetPosition(pos);
		}
	}

	public void SetPosition(Vector3 position)
	{
		Vector2 rect = target.sizeDelta;
		position.x = Mathf.Clamp(position.x, rect.x / 2, Screen.width - rect.x / 2);
		position.y = Mathf.Clamp(position.y, rect.y / 2, Screen.height - rect.y / 2);
		target.position = position;
		Groups.groupInterfaceAnchor.Value = target.localPosition;
	}

	public void OnPointerDown(PointerEventData dt)
	{
		isMouseDown = true;
		startPosition = target.position;
		target.position = startPosition;
		startMousePosition = Input.mousePosition;
	}

	public void OnPointerUp(PointerEventData dt)
	{
		isMouseDown = false;
		if (shouldReturn)
		{
			target.position = startPosition;
			Groups.groupInterfaceAnchor.Value = target.localPosition;
		}
	}
}
