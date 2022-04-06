using UnityEngine;
using UnityEngine.EventSystems;

namespace Groups;

public class DragNDrop : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
	public Transform target = null!;
	public bool shouldReturn;
	private bool isMouseDown;
	private Vector3 startMousePosition;
	private Vector3 startPosition;

	private void Start()
	{
		target = transform;
	}

	private void Update()
	{
		if (isMouseDown)
		{
			Vector3 currentPosition = Input.mousePosition;
			Vector3 diff = currentPosition - startMousePosition;
			Vector3 pos = startPosition + diff;

			target.position = pos;
		}
	}

	public void OnPointerDown(PointerEventData dt)
	{
		isMouseDown = true;
		Vector3 position = target.position;
		Groups.groupInterfaceAnchor.Value = target.localPosition;
		startPosition = position;
		startMousePosition = Input.mousePosition;
	}

	public void OnPointerUp(PointerEventData dt)
	{
		isMouseDown = false;
		if (shouldReturn)
		{
			target.position = startPosition;
		}
	}
}
