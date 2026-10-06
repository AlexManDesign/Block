const encoder=new TextEncoder();
const utf8Size=text=>encoder.encode(text).length;
function base64(bytes){let text='';for(let i=0;i<bytes.length;i+=32768)text+=String.fromCharCode(...bytes.subarray(i,i+32768));return btoa(text);}

exports["utf8Size"]=utf8Size;
exports["base64"]=base64;
