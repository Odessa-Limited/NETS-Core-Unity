// uncomment for easy debug
/*
Pointer_stringify = function(s){return s;}
autoAddDeps = function(s){}
mergeInto = function(s){}
webSocketInstances = [];
LibraryManager = []
*/
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
			this.iframe.src = "https://static.nets.odessaengine.com/ws.html?version=" + Math.random();
			document.body.appendChild(this.iframe);

			var thisObj = this;
			window.addEventListener("message", function(e) {
				//console.log(e)

				if (e.data.method == "Initialized"){
					thisObj.initialized = true;
					return;
				}

				var socket = webSocketInstances[e.data.data.instance];
				if (socket == null) console.error("Known socket: " + e.data);
				//console.log(socket);
				if (e.data.method == "onerror"){
					socket.error = e.data.data.error;
				} else if (e.data.method == "onopen"){
					socket.state = 1;
				} else if (e.data.method == "onclose"){
					socket.state = 3;
					if (e.data.data.error != null) socket.error = e.data.data.error;
				} else if (e.data.method == "onmessage"){
					socket.waitingMessages[e.data.data.seq] = e.data.data.data;
					
					while (true){
						if (socket.waitingMessages[socket.lastRecieve + 1] == null) return;
						socket.messages.push(socket.waitingMessages[socket.lastRecieve + 1]);
						delete socket.waitingMessages[socket.lastRecieve + 1];
						socket.lastRecieve++;
					}
					if (socket.waitingMessages.length > 0){
						console.log("jslib inbound delayed by " + socket.waitingMessages.length + ". " + Array.from(socket.waitingMessages.keys()).join(',') + ". lastRecieve: " + socket.lastRecieve);
					}

				}
			});

			this.created = true;
		}

		return false;
	},

	SocketCreate: function(url)
	{
		url = Pointer_stringify(url);

		var socket = {
			url: url,
			buffer: new Uint8Array(0),
			error: null,
			messages: [],
			waitingMessages: [],
			state: 0,
			lastRecieve: 0,
			lastSend: 0
		}
		var instance = webSocketInstances.push(socket) - 1;
		this.iframe.contentWindow.postMessage({method:"SocketCreate", "instance":instance, "url": url},"*");
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
		this.iframe.contentWindow.postMessage({method:"SocketSend", instance: socketInstance, seq: ++socket.lastSend, payload: HEAPU8.buffer.slice(ptr, ptr+length)},"*");
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
		this.iframe.contentWindow.postMessage({method:"SocketClose",instance:socketInstance},"*");
	}
};

autoAddDeps(LibraryWebSockets, '$webSocketInstances');
mergeInto(LibraryManager.library, LibraryWebSockets);
