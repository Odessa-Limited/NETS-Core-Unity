
var LibraryWebSockets = {
	$webSocketInstances: [],
	created: false,
	initialized: false,
	iframe: null,

	Ready: function ()
	{
		if (this.initialized == true) return true;

		if (this.created != true) {
			this.iframe = document.createElement('iframe');
			this.iframe.id = "nets_iframe"
			this.iframe.style.display = "none";
			this.iframe.src = "https://wss.nets.odessaengine.com/";
			document.body.appendChild(this.iframe);

			thisObj = this;
			window.addEventListener("message", function(e) {
				if (e.data.method == "Initialized"){
					thisObj.initialized = true;
				} else if (e.data.method == "SocketError"){
					webSocketInstances[e.data.url].error = e.data.error;
				} else if (e.data.method == "onopen"){
					webSocketInstances[e.data.url].state = 1;
				} else if (e.data.method == "onclose"){
					webSocketInstances[e.data.url].state = 3;
				} else if (e.data.method == "onmessage"){
					webSocketInstances[e.data.url].messages.push(e.data.data);
				}
			});

			this.created = true;
		}

		return false;
	},

	SocketCreate: function(url)
	{
		url = Pointer_stringify(url);

		for (i = 0; i < webSocketInstances.length; i++)
			if (webSocketInstances[i].url == url) return i;

		var socket = {
			url: url,
			buffer: new Uint8Array(0),
			error: null,
			messages: [],
			state: 0
		}
		var instance = webSocketInstances.push(socket) - 1;
		this.iframe.postMessage({method:"SocketCreate",data:url},"*");
		return instance;
	},

	SocketState: function (socketInstance)
	{
		var socket = webSocketInstances[socketInstance];
		return socket.state;
	},

	SocketError: function (socketInstance, ptr, bufsize)
	{
	 	var socket = webSocketInstances[socketInstance];
	 	if (socket.error == null) return 0;
	    var str = socket.error.slice(0, Math.max(0, bufsize - 1));
	    writeStringToMemory(str, ptr, false);
		return 1;
	},

	SocketSend: function (socketInstance, ptr, length)
	{
		var socket = webSocketInstances[socketInstance];
		this.iframe.postMessage({method:"SocketCreate",data:{url: url, data: HEAPU8.buffer.slice(ptr, ptr+length)}},"*");
	},

	SocketRecvLength: function(socketInstance)
	{
		var socket = webSocketInstances[socketInstance];
		if (socket.messages.length == 0)
			return 0;
		return socket.messages[0].length;
	},

	SocketRecv: function (socketInstance, ptr, length)
	{
		var socket = webSocketInstances[socketInstance];
		if (socket.messages.length == 0)
			return 0;
		if (socket.messages[0].length > length)
			return 0;
		HEAPU8.set(socket.messages[0], ptr);
		socket.messages = socket.messages.slice(1);
	},

	SocketClose: function (socketInstance)
	{
		var socket = webSocketInstances[socketInstance];
		this.iframe.postMessage({method:"SocketClose",data:url},"*");
	}
};

autoAddDeps(LibraryWebSockets, '$webSocketInstances');
mergeInto(LibraryManager.library, LibraryWebSockets);
