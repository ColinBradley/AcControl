const connection = new RTCPeerConnection({
    iceTransportPolicy: 'all',
    iceCandidatePoolSize: 5,
    iceServers: undefined,
    bundlePolicy: 'max-compat',
});
let remoteVideoElement: HTMLVideoElement | undefined = undefined;

(document as any).thingy = connection;
connection.addEventListener('negotiationneeded', e => {
    console.log(`onnegotiationneeded: ${JSON.stringify(e)}`);
});

connection.addEventListener('track', e => {
    if (remoteVideoElement === undefined || e.track.kind !== 'video') {
        return;
    }

    remoteVideoElement.srcObject = e.streams[0];
});

connection.addEventListener('connectionstatechange', e => {
    console.log(`State Updated: ${connection.connectionState}|${connection.signalingState}|${connection.iceConnectionState}|${JSON.stringify(e)}`);
});

connection.addEventListener('icegatheringstatechange', e => {
    console.log(`icegatheringstatechange: ${connection.iceGatheringState}`);

    if (connection.iceGatheringState === "complete") {

    }
});

connection.addEventListener('iceconnectionstatechange', e => {
    if (["disconnected", "failed", "closed"].includes(connection.iceConnectionState)) {

    }
});

connection.addTransceiver('audio', { direction: "sendrecv" });
connection.addTransceiver('video', { direction: "recvonly" });

export async function getConnectionOfferSdp() {
    const offer = await connection.createOffer({ offerToReceiveAudio: true, offerToReceiveVideo: true, iceRestart: true });
    await connection.setLocalDescription(offer);
    return offer.sdp;


    //const audioResult = await navigator.mediaDevices.getUserMedia({ audio: true, video: false });


    // // Options for SimpleUser
    // const sipOptions: Web.SimpleUserOptions = {
    //     aor: options.callInfo.sip_from,
    //     media: {
    //         constraints: { audio: true, video: true },
    //         remote: { video: options.videoElement },
    //     },
    // };

    // // Construct a SimpleUser instance
    // const simpleUser = new Web.SimpleUser(`udp://${options.callInfo.sip_server_ip}:${options.callInfo.sip_server_port}`, sipOptions);

    // // Connect to server and place call
    // await simpleUser.connect();
    // await simpleUser.call(options.callInfo.sip_to);
}

export function setVideoElement(element: HTMLVideoElement) {
    remoteVideoElement = element;
}

export async function setConnectionCallSdp(otherSdp: string) {
    await connection.setRemoteDescription(new RTCSessionDescription({ type: 'answer', sdp: otherSdp }));
}

// interface CallOptions {
//     videoElement: HTMLVideoElement;
//     callInfo: CallInformation;
// }

// interface CallInformation {
//     id: number;
//     id_str: string;
//     state: string;
//     protocol: string;
//     doorbot_id: number;
//     doorbot_description: string;
//     device_kind: string;
//     motion: boolean;
//     snapshot_url: string;
//     kind: string;
//     sip_server_ip: string;
//     sip_server_port: number;
//     sip_server_tls: boolean;
//     sip_session_id: string;
//     sip_from: string;
//     sip_to: string;
//     sip_token: string;
//     sip_ding_id: string;
//     audio_jitter_buffer_ms: number;
//     video_jitter_buffer_ms: number;
//     expires_in: number;
//     now: number;
//     optimization_level: number;
//     ding_encrypted: boolean;
// }