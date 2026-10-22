// Uploads the episode to the private "recaps" blob container. The game server hands out short-lived
// read links to it, so the container never needs public access.
import { BlobServiceClient } from '@azure/storage-blob';
import { DefaultAzureCredential } from '@azure/identity';
import { config } from './config.mjs';

function service() {
  if (config.blobConnection) return BlobServiceClient.fromConnectionString(config.blobConnection);
  if (config.blobUrl) return new BlobServiceClient(config.blobUrl, new DefaultAzureCredential());
  throw new Error('Set RECAP_BLOB_URL (or RECAP_BLOB_CONNECTION) to upload.');
}

/** Writes a small JSON document (replacing any already there). */
export async function uploadJson(value, name) {
  const body = JSON.stringify(value);
  const container = service().getContainerClient(config.container);
  await container.createIfNotExists();
  await container.getBlockBlobClient(name).upload(body, Buffer.byteLength(body), {
    blobHTTPHeaders: { blobContentType: 'application/json', blobCacheControl: 'no-cache' },
  });
}

/** Deletes a day's earlier uploads (a re-run of the same day) other than the ones named. */
export async function pruneDay(date, keep) {
  const container = service().getContainerClient(config.container);
  for await (const blob of container.listBlobsFlat({ prefix: `${date}/` })) {
    if (!keep.includes(blob.name)) await container.deleteBlob(blob.name);
  }
}

/** Uploads a file; returns its blob name. */
export async function upload(file, name, contentType) {
  const container = service().getContainerClient(config.container);
  await container.createIfNotExists();
  await container.getBlockBlobClient(name).uploadFile(file, {
    blobHTTPHeaders: { blobContentType: contentType, blobCacheControl: 'public, max-age=31536000, immutable' },
  });
  return name;
}
