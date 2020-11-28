var LibraryWebSockets = {
	
	var iframe = document.createElement('iframe');
	iframe.style.display = "none";
	iframe.src = "https://wss.nets.odessaengine.com/";
	document.body.appendChild(iframe);
	var iframeScripts = iframe.contentWindow;

	SocketCreate: function(url)
	{
		var str = Pointer_stringify(url);
		return iframeScripts.SocketCreate(str);
	},

	SocketState: function (socketInstance)
	{
		return iframeScripts.SocketState(socketInstance);
	},

	SocketError: function (socketInstance, ptr, bufsize)
	{
		return iframeScripts.SocketError(socketInstance, ptr, bufsize);
	},

	SocketSend: function (socketInstance, ptr, length)
	{
		iframeScripts.SocketSend(socketInstance, ptr, length);
	},

	SocketRecvLength: function(socketInstance)
	{
		return iframeScripts.SocketRecvLength(socketInstance);
	},

	SocketRecv: function (socketInstance, ptr, length)
	{
		iframeScripts.SocketRecv(socketInstance, ptr, length);
	},

	SocketClose: function (socketInstance)
	{
		var socket = webSocketInstances[socketInstance];
		socket.socket.close();
		iframeScripts.SocketClose(socketInstance);
	}
};

autoAddDeps(LibraryWebSockets, '$webSocketInstances');
mergeInto(LibraryManager.library, LibraryWebSockets);
